import { useRef, useState, type FormEvent } from "react";
import { useLocation } from "react-router-dom";
import {
  classifyRevisionFailure,
  getRevision,
  isRevisionId,
  transitionRevision,
  type LifecycleAction,
  type PublicationRevision,
  type RevisionFailure,
} from "../api/publicationRevisions";
import { runTransitionFlow } from "../api/revisionDeskFlow";
import { AppSurface } from "../components/AppSurface";
import { RevisionPanel, type Loadable } from "../components/PublicationRevisionView";

export function PublicationRevisionDeskPage() {
  const location = useLocation();
  const [idText, setIdText] = useState("");
  const [view, setView] = useState<Loadable<PublicationRevision>>({ kind: "idle" });
  const [busy, setBusy] = useState(false);
  const [notice, setNotice] = useState<string | null>(null);
  const [actionFailure, setActionFailure] = useState<RevisionFailure | null>(null);
  const latest = useRef(0);
  const signInHref = `/login?returnTo=${encodeURIComponent(`${location.pathname}${location.search}`)}`;

  async function load(id: string, keepNotice = false) {
    const ticket = ++latest.current;
    setView({ kind: "loading" });
    if (!keepNotice) setNotice(null);
    setActionFailure(null);
    try {
      const data = await getRevision(id);
      if (ticket === latest.current) setView({ kind: "loaded", data });
    } catch (error) {
      if (ticket === latest.current) setView({ kind: "error", failure: classifyRevisionFailure(error, navigator.onLine) });
    }
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
