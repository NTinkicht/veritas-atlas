import { useEffect, useRef, useState } from "react";
import { useLocation } from "react-router-dom";
import {
  addAnnotation,
  draftAfterSave,
  classifyInvestigationFailure,
  createInvestigation,
  listAnnotations,
  type EvidenceAnnotation,
  compareEvidenceItems,
  listBookmarks,
  listEvidenceItems,
  putBookmark,
  removeBookmark,
  type InvestigationFailure,
  type EvidenceComparison as EvidenceComparisonData,
  listInvestigations,
  type CollectedEvidenceItem,
  type Investigation,
} from "../api/investigations";
import { AppSurface } from "../components/AppSurface";
import { CreateInvestigationForm, EvidenceBoard, EvidenceComparison, InvestigationList, type AnnotationsState, type Loadable } from "../components/InvestigationBoardView";

export function InvestigationBoardPage() {
  const location = useLocation();
  const [list, setList] = useState<Loadable<Investigation[]>>({ kind: "loading" });
  const [selected, setSelected] = useState<Investigation | null>(null);
  const [board, setBoard] = useState<Loadable<CollectedEvidenceItem[]>>({ kind: "loading" });
  const [picked, setPicked] = useState<string[]>([]);
  const [comparison, setComparison] = useState<Loadable<EvidenceComparisonData> | null>(null);
  const [bookmarked, setBookmarked] = useState<string[]>([]);
  const [bookmarksReady, setBookmarksReady] = useState(false);
  const [busy, setBusy] = useState<string[]>([]);
  const [bookmarkNotice, setBookmarkNotice] = useState<InvestigationFailure | null>(null);
  const [annotationItem, setAnnotationItem] = useState<string | null>(null);
  const [annotationView, setAnnotationView] = useState<Loadable<EvidenceAnnotation[]>>({ kind: "loading" });
  const [draft, setDraft] = useState("");
  const [submitting, setSubmitting] = useState(false);
  const [annotationNotice, setAnnotationNotice] = useState<InvestigationFailure | null>(null);
  const [newTitle, setNewTitle] = useState("");
  const [creating, setCreating] = useState(false);
  const [createNotice, setCreateNotice] = useState<InvestigationFailure | null>(null);
  // Same title keeps the same key so a retry replays instead of duplicating; success or an edited title rotates it.
  const creationKey = useRef<{ title: string; key: string } | null>(null);
  const latestAnnotations = useRef(0);
  const latest = useRef(0);
  const latestComparison = useRef(0);
  const signInHref = `/login?returnTo=${encodeURIComponent(`${location.pathname}${location.search}`)}`;

  useEffect(() => {
    let live = true;
    listInvestigations().then(
      (data) => live && setList({ kind: "loaded", data }),
      (error) => live && setList({ kind: "error", failure: classifyInvestigationFailure(error, navigator.onLine) }),
    );
    return () => {
      live = false;
    };
  }, []);

  function open(investigation: Investigation) {
    const ticket = ++latest.current;
    latestComparison.current++;
    setPicked([]);
    setComparison(null);
    setBookmarked([]);
    setBookmarksReady(false);
    setBusy([]);
    setBookmarkNotice(null);
    latestAnnotations.current++;
    setAnnotationItem(null);
    setDraft("");
    setSubmitting(false);
    setAnnotationNotice(null);
    setSelected(investigation);
    setBoard({ kind: "loading" });
    // A failed bookmark read is shown, never treated as "no bookmarks".
    listBookmarks(investigation.id).then(
      (data) => {
        if (ticket !== latest.current) return;
        setBookmarked(data.map((b) => b.evidenceItemId));
        setBookmarksReady(true);
      },
      (error) =>
        ticket === latest.current && setBookmarkNotice(classifyInvestigationFailure(error, navigator.onLine)),
    );
    listEvidenceItems(investigation.id).then(
      (data) => ticket === latest.current && setBoard({ kind: "loaded", data }),
      (error) =>
        ticket === latest.current &&
        setBoard({ kind: "error", failure: classifyInvestigationFailure(error, navigator.onLine) }),
    );
  }

  function toggleBookmark(itemId: string, next: boolean) {
    if (!selected || !bookmarksReady) return;
    const ticket = latest.current;
    const request = next ? putBookmark(selected.id, itemId).then(() => undefined) : removeBookmark(selected.id, itemId);
    setBusy((current) => [...current, itemId]);
    setBookmarkNotice(null);
    request.then(
      () => {
        if (ticket !== latest.current) return;
        setBookmarked((current) => (next ? [...current.filter((id) => id !== itemId), itemId] : current.filter((id) => id !== itemId)));
        setBusy((current) => current.filter((id) => id !== itemId));
      },
      (error) => {
        if (ticket !== latest.current) return;
        setBookmarkNotice(classifyInvestigationFailure(error, navigator.onLine));
        setBusy((current) => current.filter((id) => id !== itemId));
      },
    );
  }

  function toggleAnnotations(itemId: string) {
    if (!selected) return;
    // Any change of the open item invalidates a read or write still in flight for the previous one.
    const ticket = ++latestAnnotations.current;
    setDraft("");
    setSubmitting(false);
    setAnnotationNotice(null);
    if (annotationItem === itemId) {
      setAnnotationItem(null);
      return;
    }
    setAnnotationItem(itemId);
    setAnnotationView({ kind: "loading" });
    listAnnotations(selected.id, itemId).then(
      (data) => ticket === latestAnnotations.current && setAnnotationView({ kind: "loaded", data }),
      (error) =>
        ticket === latestAnnotations.current &&
        setAnnotationView({ kind: "error", failure: classifyInvestigationFailure(error, navigator.onLine) }),
    );
  }

  function submitNewInvestigation() {
    if (creating) return;
    const title = newTitle;
    if (creationKey.current?.title !== title) creationKey.current = { title, key: crypto.randomUUID() };
    const key = creationKey.current.key;
    setCreating(true);
    setCreateNotice(null);
    createInvestigation(title, key).then(
      (created) => {
        creationKey.current = null;
        setNewTitle((now) => (now === title ? "" : now));
        setList((current) =>
          current.kind === "loaded"
            ? { kind: "loaded", data: [created, ...current.data.filter((i) => i.id !== created.id)] }
            : current,
        );
        setCreating(false);
        open(created);
      },
      (error) => {
        setCreateNotice(classifyInvestigationFailure(error, navigator.onLine));
        setCreating(false);
      },
    );
  }

  function submitAnnotation() {
    if (!selected || !annotationItem || submitting || annotationView.kind !== "loaded") return;
    const ticket = ++latestAnnotations.current;
    const current = annotationView.data;
    const submitted = draft;
    setSubmitting(true);
    setAnnotationNotice(null);
    addAnnotation(selected.id, annotationItem, submitted).then(
      (created) => {
        if (ticket !== latestAnnotations.current) return;
        // A replayed answer returns the stored row: never show it twice.
        setAnnotationView({ kind: "loaded", data: [...current.filter((a) => a.id !== created.id), created] });
        setDraft((now) => draftAfterSave(now, submitted));
        setSubmitting(false);
      },
      (error) => {
        if (ticket !== latestAnnotations.current) return;
        setAnnotationNotice(classifyInvestigationFailure(error, navigator.onLine));
        setSubmitting(false);
      },
    );
  }

  const annotations: AnnotationsState | null = annotationItem
    ? {
        itemId: annotationItem,
        view: annotationView,
        draft,
        submitting,
        notice: annotationNotice,
        onDraftChange: setDraft,
        onSubmit: submitAnnotation,
      }
    : null;

  function togglePick(itemId: string) {
    // Any change to the selection invalidates a comparison already shown or in flight.
    latestComparison.current++;
    setComparison(null);
    setPicked((current) =>
      current.includes(itemId) ? current.filter((id) => id !== itemId) : current.length >= 2 ? current : [...current, itemId],
    );
  }

  function compare() {
    if (!selected || picked.length !== 2) return;
    const ticket = ++latestComparison.current;
    setComparison({ kind: "loading" });
    compareEvidenceItems(selected.id, picked[0], picked[1]).then(
      (data) => ticket === latestComparison.current && setComparison({ kind: "loaded", data }),
      (error) =>
        ticket === latestComparison.current &&
        setComparison({ kind: "error", failure: classifyInvestigationFailure(error, navigator.onLine) }),
    );
  }

  return (
    <AppSurface title="Investigations" subtitle="Investigations visible to you and the verified evidence collected into them.">
      <CreateInvestigationForm
        title={newTitle}
        submitting={creating}
        notice={createNotice}
        onTitleChange={setNewTitle}
        onSubmit={submitNewInvestigation}
        signInHref={signInHref}
      />
      <InvestigationList view={list} selectedId={selected?.id ?? null} onSelect={open} signInHref={signInHref} />
      {selected ? (
        <EvidenceBoard
          investigation={selected}
          view={board}
          signInHref={signInHref}
          pickedIds={picked}
          onTogglePick={togglePick}
          bookmarkedIds={bookmarked}
          onToggleBookmark={toggleBookmark}
          busyIds={busy}
          bookmarksReady={bookmarksReady}
          bookmarkNotice={bookmarkNotice}
          annotations={annotations}
          onToggleAnnotations={toggleAnnotations}
        />
      ) : null}
      {selected && board.kind === "loaded" && board.data.length >= 2 ? (
        <button type="button" disabled={picked.length !== 2} onClick={compare}>
          Compare selected items
        </button>
      ) : null}
      {comparison ? <EvidenceComparison view={comparison} signInHref={signInHref} /> : null}
    </AppSurface>
  );
}
