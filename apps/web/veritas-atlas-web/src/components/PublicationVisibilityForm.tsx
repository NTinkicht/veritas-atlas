import { useState, type FormEvent } from "react";
import { classifyRevisionFailure, type PublicationRevision, type RevisionFailure } from "../api/publicationRevisions";
import { type VisibilityInput, type VisibilityCitationInput } from "../api/publicationVisibility";

// VER-P0-17: publisher sets what the anonymous public reader may see. Fails closed: starts
// restricted, success is reported only after the server accepted the write, and the server alone
// decides who may do it. Shown only for Approved/Published revisions (the only legal states).

// The owner (desk page) performs the write so a late completion can be dropped when another revision
// was opened meanwhile and the confirmation survives the reload that unmounts this form.
type Props = {
  revision: PublicationRevision;
  onSave: (revision: PublicationRevision, input: VisibilityInput) => Promise<void>;
  signInHref: string;
};

const blank = (): VisibilityCitationInput => ({ citationLabel: "", title: "", quote: "", sourceName: "", freshnessLabel: "" });

export function PublicationVisibilityForm({ revision, onSave, signInHref }: Props) {
  const [restricted, setRestricted] = useState(true);
  const [superseded, setSuperseded] = useState(false);
  const [freshnessRequired, setFreshnessRequired] = useState(true);
  const [currency, setCurrency] = useState<"Current" | "NonCurrent" | "Unknown">("Unknown");
  const [historical, setHistorical] = useState(false);
  const [citations, setCitations] = useState<VisibilityCitationInput[]>([blank()]);
  const [busy, setBusy] = useState(false);
  const [failure, setFailure] = useState<RevisionFailure | null>(null);

  if (revision.state !== "Approved" && revision.state !== "Published") return null;

  const edit = (i: number, patch: Partial<VisibilityCitationInput>) =>
    setCitations((rows) => rows.map((r, j) => (j === i ? { ...r, ...patch } : r)));

  async function submit(event: FormEvent) {
    event.preventDefault();
    if (busy) return;
    setBusy(true);
    setFailure(null);
    try {
      await onSave(revision, { restricted, superseded, freshnessRequired, evidenceCurrency: currency, historicalLabelPresent: historical, citations });
    } catch (error) {
      setFailure(classifyRevisionFailure(error, navigator.onLine));
    } finally {
      setBusy(false);
    }
  }

  const field = (label: string, key: keyof VisibilityCitationInput, i: number, area = false) => (
    <label key={key}>
      {label}{" "}
      {area ? (
        <textarea value={citations[i][key] ?? ""} onChange={(e) => edit(i, { [key]: e.target.value })} />
      ) : (
        <input value={citations[i][key] ?? ""} onChange={(e) => edit(i, { [key]: e.target.value })} autoComplete="off" />
      )}
    </label>
  );

  return (
    <form onSubmit={submit} aria-label="Public visibility">
      <h3>Public reader visibility (publisher)</h3>
      <p className="muted">Starts restricted. Unrestricting needs policy permission and at least one citation; the server decides.</p>
      <label><input type="checkbox" checked={restricted} onChange={(e) => setRestricted(e.target.checked)} /> Restricted (hidden from the public)</label>{" "}
      <label><input type="checkbox" checked={superseded} onChange={(e) => setSuperseded(e.target.checked)} /> Superseded</label>{" "}
      <label><input type="checkbox" checked={freshnessRequired} onChange={(e) => setFreshnessRequired(e.target.checked)} /> Freshness required</label>{" "}
      <label><input type="checkbox" checked={historical} onChange={(e) => setHistorical(e.target.checked)} /> Historical label present</label>{" "}
      <label>
        Evidence currency{" "}
        <select value={currency} onChange={(e) => setCurrency(e.target.value as typeof currency)}>
          <option value="Current">Current</option>
          <option value="NonCurrent">NonCurrent</option>
          <option value="Unknown">Unknown</option>
        </select>
      </label>
      {citations.map((_, i) => (
        <fieldset key={i}>
          <legend>Citation {i + 1}</legend>
          {field("Citation label", "citationLabel", i)} {field("Title", "title", i)} {field("Source name", "sourceName", i)}{" "}
          {field("Freshness label", "freshnessLabel", i)} {field("Quote", "quote", i, true)}
          {field("Contradiction disclosure", "contradictionDisclosure", i, true)} {field("Uncertainty limit", "uncertaintyLimit", i, true)}
          <button type="button" onClick={() => setCitations((rows) => rows.filter((_, j) => j !== i))}>Remove citation {i + 1}</button>
        </fieldset>
      ))}
      <button type="button" onClick={() => setCitations((rows) => [...rows, blank()])}>Add citation</button>{" "}
      <button type="submit" disabled={busy}>Save visibility</button>
      {failure ? (
        <p className="notice-card notice-danger" role="alert">
          {failure.message}
          {failure.kind === "unauthenticated" ? <> <a href={signInHref}>Sign in</a></> : null}
        </p>
      ) : null}
    </form>
  );
}
