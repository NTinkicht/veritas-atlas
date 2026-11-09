import { useEffect, useRef, useState } from "react";
import { useLocation } from "react-router-dom";
import {
  classifyInvestigationFailure,
  listEvidenceItems,
  listInvestigations,
  type CollectedEvidenceItem,
  type Investigation,
} from "../api/investigations";
import { AppSurface } from "../components/AppSurface";
import { EvidenceBoard, InvestigationList, type Loadable } from "../components/InvestigationBoardView";

export function InvestigationBoardPage() {
  const location = useLocation();
  const [list, setList] = useState<Loadable<Investigation[]>>({ kind: "loading" });
  const [selected, setSelected] = useState<Investigation | null>(null);
  const [board, setBoard] = useState<Loadable<CollectedEvidenceItem[]>>({ kind: "loading" });
  const latest = useRef(0);
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
    setSelected(investigation);
    setBoard({ kind: "loading" });
    listEvidenceItems(investigation.id).then(
      (data) => ticket === latest.current && setBoard({ kind: "loaded", data }),
      (error) =>
        ticket === latest.current &&
        setBoard({ kind: "error", failure: classifyInvestigationFailure(error, navigator.onLine) }),
    );
  }

  return (
    <AppSurface title="Investigations" subtitle="Investigations visible to you and the verified evidence collected into them.">
      <InvestigationList view={list} selectedId={selected?.id ?? null} onSelect={open} signInHref={signInHref} />
      {selected ? <EvidenceBoard investigation={selected} view={board} signInHref={signInHref} /> : null}
    </AppSurface>
  );
}
