import { useRef, useState, type FormEvent } from "react";
import { useLocation } from "react-router-dom";
import { classifyCandidatesFailure, listClaimCandidates } from "../api/claimCandidates";
import { AppSurface } from "../components/AppSurface";
import { ClaimCandidatesResultView, type CandidatesView } from "../components/ClaimCandidatesView";

export function ClaimCandidatesPage() {
  const location = useLocation();
  const [evidenceRef, setEvidenceRef] = useState("");
  const [view, setView] = useState<CandidatesView>({ kind: "idle" });
  const latest = useRef(0);
  const signInHref = `/login?returnTo=${encodeURIComponent(`${location.pathname}${location.search}`)}`;

  function submit(event: FormEvent) {
    event.preventDefault();
    const ticket = ++latest.current;
    setView({ kind: "loading" });
    listClaimCandidates(evidenceRef).then(
      (result) => ticket === latest.current && setView({ kind: "loaded", result }),
      (error) => ticket === latest.current && setView({ kind: "error", failure: classifyCandidatesFailure(error, navigator.onLine) }),
    );
  }

  return (
    <AppSurface title="Claim candidates" subtitle="List the exact quotations the extractor finds in one verified evidence item. Candidates are unconfirmed and nothing is saved.">
      <form onSubmit={submit}>
        <p>
          <label>Evidence reference <input value={evidenceRef} onChange={(e) => setEvidenceRef(e.target.value)} spellCheck={false} autoComplete="off" /></label>
        </p>
        <button type="submit">List claim candidates</button>
      </form>
      <ClaimCandidatesResultView view={view} signInHref={signInHref} />
    </AppSurface>
  );
}
