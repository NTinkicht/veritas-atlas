import { useRef, useState, type FormEvent } from "react";
import { useLocation } from "react-router-dom";
import { MAX_CLAIMS, buildRelationGraph, classifyGraphFailure, emptyClaim, type ClaimInput } from "../api/claimRelationGraph";
import { AppSurface } from "../components/AppSurface";
import { GraphResult, type GraphView } from "../components/ClaimRelationGraphView";

const TEXT_FIELDS: { key: Exclude<keyof ClaimInput, "negated">; label: string }[] = [
  { key: "claimId", label: "Claim id" },
  { key: "evidenceRef", label: "Evidence reference" },
  { key: "sourceId", label: "Source id (optional)" },
  { key: "subject", label: "Subject" },
  { key: "entityKey", label: "Resolved entity key (optional)" },
  { key: "predicate", label: "Predicate" },
  { key: "value", label: "Value (optional)" },
  { key: "asOf", label: "As-of date YYYY-MM-DD (optional)" },
];

export function ClaimRelationGraphPage() {
  const location = useLocation();
  const [claims, setClaims] = useState<ClaimInput[]>([emptyClaim(), emptyClaim()]);
  const [view, setView] = useState<GraphView>({ kind: "idle" });
  const latest = useRef(0);
  const signInHref = `/login?returnTo=${encodeURIComponent(`${location.pathname}${location.search}`)}`;

  const update = (i: number, patch: Partial<ClaimInput>) => setClaims((all) => all.map((c, j) => (j === i ? { ...c, ...patch } : c)));

  function submit(event: FormEvent) {
    event.preventDefault();
    const ticket = ++latest.current;
    setView({ kind: "loading" });
    buildRelationGraph(claims).then(
      (graph) => ticket === latest.current && setView({ kind: "loaded", graph }),
      (error) => ticket === latest.current && setView({ kind: "error", failure: classifyGraphFailure(error, navigator.onLine) }),
    );
  }

  return (
    <AppSurface title="Claim relations" subtitle="Compare structured claims to see which agree, conflict, supersede one another or cannot be resolved. Evidence references are not verified here.">
      <form onSubmit={submit}>
        {claims.map((claim, i) => (
          <fieldset key={i}>
            <legend>Claim {i + 1}</legend>
            {TEXT_FIELDS.map(({ key, label }) => (
              <p key={key}>
                <label>
                  {label}{" "}
                  <input value={claim[key]} onChange={(e) => update(i, { [key]: e.target.value })} spellCheck={false} autoComplete="off" />
                </label>
              </p>
            ))}
            <p>
              <label>
                <input type="checkbox" checked={claim.negated} onChange={(e) => update(i, { negated: e.target.checked })} /> Claim is negated
              </label>
            </p>
            {claims.length > 1 ? (
              <button type="button" onClick={() => setClaims((all) => all.filter((_, j) => j !== i))}>
                Remove claim {i + 1}
              </button>
            ) : null}
          </fieldset>
        ))}
        <p>
          <button type="button" disabled={claims.length >= MAX_CLAIMS} onClick={() => setClaims((all) => [...all, emptyClaim()])}>
            Add claim
          </button>{" "}
          <button type="submit">Compare claims</button>
        </p>
      </form>
      <GraphResult view={view} signInHref={signInHref} />
    </AppSurface>
  );
}
