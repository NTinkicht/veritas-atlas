import { useEffect, useRef, useState, type FormEvent } from "react";
import { useLocation } from "react-router-dom";
import {
  classifyRevisionFailure,
  getRevision,
  isRevisionId,
  listRevisions,
  transitionRevision,
  type LifecycleAction,
  type PublicationRevision,
  type RevisionFailure,
  type RevisionState,
} from "../api/publicationRevisions";
import { reconcileQueueItem, runTransitionFlow } from "../api/revisionDeskFlow";
import { AppSurface } from "../components/AppSurface";
import { RevisionQueue, type QueueView } from "../components/PublicationRevisionQueue";
import { RevisionPanel, type Loadable } from "../components/PublicationRevisionView";

export function PublicationRevisionDeskPage() {
  const location = useLocation();
  const [idText, setIdText] = useState("");
  const [view, setView] = useState<Loadable<PublicationRevision>>({ kind: "idle" });
  const [busy, setBusy] = useState(false);
  const [notice, setNotice] = useState<string | null>(null);
  const [actionFailure, setActionFailure] = useState<RevisionFailure | null>(null);
  const latest = useRef(0);
  const [queueState, setQueueState] = useState<RevisionState | null>(null);
  const [queue, setQueue] = useState<QueueView>({ items: [], nextAfterId: null, loading: false, failure: null, loaded: false });
  const queueTicket = useRef(0);
  const queueStateRef = useRef<RevisionState | null>(null);
  queueStateRef.current = queueState;
  const signInHref = `/login?returnTo=${encodeURIComponent(`${location.pathname}${location.search}`)}`;

  async function load(id: string, keepNotice = false) {
    const ticket = ++latest.current;
    setView({ kind: "loading" });
    if (!keepNotice) setNotice(null);
    setActionFailure(null);
    try {
      const data = await getRevision(id);
      if (ticket === latest.current) setView({ kind: "loaded", data });
      setQueue((q) => ({ ...q, items: reconcileQueueItem(q.items, data, queueStateRef.current) }));
    } catch (error) {
      if (ticket === latest.current) setView({ kind: "error", failure: classifyRevisionFailure(error, navigator.onLine) });
    }
  }

  async function loadQueue(state: RevisionState | null, afterId: string | null) {
    const ticket = ++queueTicket.current;
    setQueue((q) => ({ ...q, items: afterId === null ? [] : q.items, nextAfterId: afterId === null ? null : q.nextAfterId, loading: true, failure: null }));
    try {
      const page = await listRevisions(state, afterId);
      if (ticket !== queueTicket.current) return;
      setQueue((q) => {
        const seen = new Set(afterId === null ? [] : q.items.map((i) => i.id));
        const fresh = page.items.filter((i) => !seen.has(i.id));
        return { items: afterId === null ? fresh : [...q.items, ...fresh], nextAfterId: page.nextAfterId, loading: false, failure: null, loaded: true };
      });
    } catch (error) {
      if (ticket === queueTicket.current) {
        setQueue((q) => ({ ...q, loading: false, loaded: true, failure: classifyRevisionFailure(error, navigator.onLine) }));
      }
    }
  }

  useEffect(() => {
    void loadQueue(queueState, null);
  }, [queueState]);

  function openFromQueue(id: string) {
    setIdText(id);
    void load(id);
  }

  function submit(event: FormEvent) {
    event.preventDefault();
    const id = idText.trim();
    if (!isRevisionId(id)) {
      latest.current += 1;
      setNotice(null);
      setActionFailure(null);
      setView({ kind: "error", failure: { kind: "malformed", message: "A revision id is a GUID such as 11111111-1111-4111-8111-111111111111." } });
      return;
    }
    void load(id);
  }

  async function act(action: LifecycleAction) {
    if (view.kind !== "loaded" || busy) return;
    const { id, version } = view.data;
    const startedOn = latest.current;
    setBusy(true);
    setNotice(null);
    setActionFailure(null);
    try {
      await runTransitionFlow({
        id, action, version, operationId: crypto.randomUUID(),
        isCurrent: () => latest.current === startedOn,
        transition: transitionRevision,
        classify: (error) => classifyRevisionFailure(error, navigator.onLine),
        onAccepted: setNotice,
        onRefused: setActionFailure,
        reload: (revisionId) => load(revisionId, true),
      });
    } finally {
      setBusy(false);
    }
  }

  return (
    <AppSurface title="Publication revisions" subtitle="Open a publication revision and move it through review and publication. The server decides every action.">
      <RevisionQueue
        queue={queue}
        state={queueState}
        onStateChange={setQueueState}
        onOpen={openFromQueue}
        onLoadMore={() => void loadQueue(queueState, queue.nextAfterId)}
        signInHref={signInHref}
      />
      <form onSubmit={submit}>
        <label>
          Revision id{" "}
          <input value={idText} onChange={(e) => setIdText(e.target.value)} spellCheck={false} autoComplete="off" />
        </label>{" "}
        <button type="submit">Open</button>
      </form>
      <RevisionPanel view={view} busy={busy} notice={notice} actionFailure={actionFailure} onAction={act} signInHref={signInHref} />
    </AppSurface>
  );
}
