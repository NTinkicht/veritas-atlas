import { useRef, useState, type FormEvent } from "react";
import { useLocation } from "react-router-dom";
import { COUNT_FIELDS, classifyConfidenceFailure, labelClaimConfidence, type CountField } from "../api/claimConfidence";
import { AppSurface } from "../components/AppSurface";
import { ConfidenceResult, type ConfidenceView } from "../components/ClaimConfidenceView";

const LABELS: Record<CountField, string> = {
  supportingEvidenceCount: "Supporting evidence",
  independentSourceCount: "Independent sources",
  conflictingCount: "Conflicting evidence",
  supersededCount: "Superseded evidence",
  staleCount: "Stale evidence",
  unknownFreshnessCount: "Unknown-freshness evidence",
  unresolvedRelationCount: "Unresolved relations",
};

export function ClaimConfidencePage() {
  const location = useLocation();
  const [claimId, setClaimId] = useState("");
  const [counts, setCounts] = useState<Partial<Record<CountField, string>>>({});
  const [view, setView] = useState<ConfidenceView>({ kind: "idle" });
  const latest = useRef(0);
  const signInHref = `/login?returnTo=${encodeURIComponent(`${location.pathname}${location.search}`)}`;

  function submit(event: FormEvent) {
    event.preventDefault();
    const ticket = ++latest.current;
    setView({ kind: "loading" });
    labelClaimConfidence(claimId, counts).then(
      (label) => ticket === latest.current && setView({ kind: "loaded", label }),
      (error) => ticket === latest.current && setView({ kind: "error", failure: classifyConfidenceFailure(error, navigator.onLine) }),
    );
  }

  return (
    <AppSurface title="Claim confidence" subtitle="Label how well the evidence signals for one claim support it. Leave a count blank when it is not available.">
      <form onSubmit={submit}>
        <p>
          <label>
            Claim id{" "}
            <input value={claimId} onChange={(e) => setClaimId(e.target.value)} spellCheck={false} autoComplete="off" />
          </label>
        </p>
        {COUNT_FIELDS.map((field) => (
          <p key={field}>
            <label>
              {LABELS[field]}{" "}
              <input
                inputMode="numeric"
                value={counts[field] ?? ""}
                onChange={(e) => setCounts((c) => ({ ...c, [field]: e.target.value }))}
                autoComplete="off"
              />
            </label>
          </p>
        ))}
        <button type="submit">Label confidence</button>
      </form>
      <ConfidenceResult view={view} signInHref={signInHref} />
    </AppSurface>
  );
}
