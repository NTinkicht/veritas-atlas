import { useEffect, useRef, useState } from "react";
import { useLocation } from "react-router-dom";
import {
  classifyInvestigationFailure,
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
import { EvidenceBoard, EvidenceComparison, InvestigationList, type Loadable } from "../components/InvestigationBoardView";

export function InvestigationBoardPage() {
  const location = useLocation();
  const [list, setList] = useState<Loadable<Investigation[]>>({ kind: "loading" });
  const [selected, setSelected] = useState<Investigation | null>(null);
  const [board, setBoard] = useState<Loadable<CollectedEvidenceItem[]>>({ kind: "loading" });
  const [picked, setPicked] = useState<string[]>([]);
  const [comparison, setComparison] = useState<Loadable<EvidenceComparisonData> | null>(null);
  const [bookmarked, setBookmarked] = useState<string[]>([]);
  const [busy, setBusy] = useState<string[]>([]);
  const [bookmarkNotice, setBookmarkNotice] = useState<InvestigationFailure | null>(null);
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
    setBusy([]);
    setBookmarkNotice(null);
    setSelected(investigation);
    setBoard({ kind: "loading" });
    // A failed bookmark read is shown, never treated as "no bookmarks".
    listBookmarks(investigation.id).then(
      (data) => ticket === latest.current && setBookmarked(data.map((b) => b.evidenceItemId)),
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
    if (!selected) return;
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
          bookmarkNotice={bookmarkNotice}
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
