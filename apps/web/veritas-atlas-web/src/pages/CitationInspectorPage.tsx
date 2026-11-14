import { useRef, useState, type FormEvent } from "react";
import { useLocation } from "react-router-dom";
import { MAX_CITATIONS, classifyCitationFailure, inspectCitations, type CitationPolicy, type CitationRow } from "../api/citationInspector";
import { AppSurface } from "../components/AppSurface";
import { CitationResult, type CitationView } from "../components/CitationInspectorView";

const blankRow = (): CitationRow => ({ evidenceRef: "", parserName: "", parserVersion: "", scopeSourceEntryId: "" });

export function CitationInspectorPage() {
  const location = useLocation();
  const [policy, setPolicy] = useState<CitationPolicy>({ evidenceRequired: true, historicalEvidenceAllowed: false, historicalTimeContextPreserved: false });
  const [rows, setRows] = useState<CitationRow[]>([blankRow()]);
  const [view, setView] = useState<CitationView>({ kind: "idle" });
  const latest = useRef(0);
  const signInHref = `/login?returnTo=${encodeURIComponent(`${location.pathname}${location.search}`)}`;

  function update(index: number, field: keyof CitationRow, value: string) {
    setRows((current) => current.map((row, i) => (i === index ? { ...row, [field]: value } : row)));
  }

  function submit(event: FormEvent) {
    event.preventDefault();
    const ticket = ++latest.current;
    setView({ kind: "loading" });
    inspectCitations(policy, rows).then(
      (verdict) => ticket === latest.current && setView({ kind: "loaded", verdict }),
      (error) => ticket === latest.current && setView({ kind: "error", failure: classifyCitationFailure(error, navigator.onLine) }),
    );
  }

  const flag = (field: keyof CitationPolicy, label: string) => (
    <p>
      <label>
        <input type="checkbox" checked={policy[field]} onChange={(e) => setPolicy((p) => ({ ...p, [field]: e.target.checked }))} /> {label}
      </label>
    </p>
  );

  return (
    <AppSurface title="Citation inspector" subtitle="Ask the server whether a claim's cited evidence is valid, in scope and fresh enough. Nothing is verified in your browser.">
      <form onSubmit={submit}>
        {flag("evidenceRequired", "This claim requires evidence")}
        {flag("historicalEvidenceAllowed", "Historical (non-current) evidence is allowed")}
        {flag("historicalTimeContextPreserved", "The historical time context is preserved in the answer")}
        {rows.map((row, i) => (
          <fieldset key={i}>
            <legend>Citation {i + 1}</legend>
            <p>
              <label>
                Evidence reference {i + 1}{" "}
                <input value={row.evidenceRef} onChange={(e) => update(i, "evidenceRef", e.target.value)} spellCheck={false} autoComplete="off" />
              </label>
            </p>
            <p>
              <label>
                Parser name {i + 1}{" "}
                <input value={row.parserName} onChange={(e) => update(i, "parserName", e.target.value)} spellCheck={false} autoComplete="off" />
              </label>
            </p>
            <p>
              <label>
                Parser version {i + 1}{" "}
                <input value={row.parserVersion} onChange={(e) => update(i, "parserVersion", e.target.value)} spellCheck={false} autoComplete="off" />
              </label>
            </p>
            <p>
              <label>
                Scope source entry {i + 1} (optional){" "}
                <input value={row.scopeSourceEntryId} onChange={(e) => update(i, "scopeSourceEntryId", e.target.value)} spellCheck={false} autoComplete="off" />
              </label>
            </p>
            <button type="button" onClick={() => setRows((c) => c.filter((_, j) => j !== i))}>Remove citation {i + 1}</button>
          </fieldset>
        ))}
        <p>
          <button type="button" disabled={rows.length >= MAX_CITATIONS} onClick={() => setRows((c) => [...c, blankRow()])}>Add citation</button>
        </p>
        <button type="submit">Inspect citations</button>
      </form>
      <CitationResult view={view} signInHref={signInHref} />
    </AppSurface>
  );
}
