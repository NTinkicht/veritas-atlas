import { formatInstant } from "../api/evidenceSearch";
import type { CollectedEvidenceItem, Investigation, InvestigationFailure } from "../api/investigations";

// Presentational pieces of the investigation board (VER-P0-17). All API text (titles, notes,
// subjects) is rendered as React text children, never as HTML.

export type Loadable<T> =
  | { kind: "loading" }
  | { kind: "error"; failure: InvestigationFailure }
  | { kind: "loaded"; data: T };

export function FailureNotice({ failure, signInHref }: { failure: InvestigationFailure; signInHref: string }) {
  return (
    <p className="notice-card notice-danger" role="alert">
      {failure.message}
      {failure.kind === "unauthenticated" ? <> <a href={signInHref}>Sign in</a></> : null}
    </p>
  );
}

type ListProps = {
  view: Loadable<Investigation[]>;
  selectedId: string | null;
  onSelect: (investigation: Investigation) => void;
  signInHref: string;
};

export function InvestigationList({ view, selectedId, onSelect, signInHref }: ListProps) {
  if (view.kind === "loading") return <p className="notice-card" role="status">Loading investigations...</p>;
  if (view.kind === "error") return <FailureNotice failure={view.failure} signInHref={signInHref} />;
  if (view.data.length === 0) return <p className="empty-card">No investigations are visible to you yet.</p>;
  return (
    <ul className="es-results" aria-label="Investigations">
      {view.data.map((investigation) => (
        <li key={investigation.id}>
          <button type="button" aria-pressed={investigation.id === selectedId} onClick={() => onSelect(investigation)}>
            {investigation.title}
          </button>
          <span className="muted"> opened {formatInstant(investigation.createdAtUtc)} by {investigation.createdBySubject}</span>
        </li>
      ))}
    </ul>
  );
}

type BoardProps = { investigation: Investigation; view: Loadable<CollectedEvidenceItem[]>; signInHref: string };

export function EvidenceBoard({ investigation, view, signInHref }: BoardProps) {
  return (
    <section className="panel" aria-label={`Collected evidence for ${investigation.title}`}>
      <h2>{investigation.title}</h2>
      {view.kind === "loading" ? <p className="notice-card" role="status">Loading collected evidence...</p> : null}
      {view.kind === "error" ? <FailureNotice failure={view.failure} signInHref={signInHref} /> : null}
      {view.kind === "loaded" && view.data.length === 0 ? (
        <p className="empty-card">No evidence has been collected into this investigation yet.</p>
      ) : null}
      {view.kind === "loaded" && view.data.length > 0 ? (
        <ul aria-label="Collected evidence">
          {view.data.map((item) => (
            <li key={item.id}>
              <strong>Verified evidence {item.evidenceVerificationId}</strong>
              <div className="muted">collected {formatInstant(item.createdAtUtc)} by {item.collectedBySubject}</div>
              {item.note ? <p>{item.note}</p> : null}
            </li>
          ))}
        </ul>
      ) : null}
    </section>
  );
}
