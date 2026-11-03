import {
  formatInstant,
  isPresentedAsCurrent,
  type EvidenceHit,
  type FreshnessLabel,
  type SearchFailure,
} from "../api/evidenceSearch";

// Presentational pieces of the analyst evidence search (VER-P0-17, #161). All source-document
// text (URIs, references, reasons) is rendered as React text children, never as HTML.

export type SearchView =
  | { kind: "idle" }
  | { kind: "loading" }
  | { kind: "error"; failure: SearchFailure }
  | { kind: "loaded"; hits: EvidenceHit[]; nextCursor: string | null; loadingMore: boolean; moreFailure: SearchFailure | null };

const FRESHNESS_TEXT: Record<FreshnessLabel, string> = {
  CURRENT: "Current",
  STALE: "Stale",
  SUPERSEDED: "Superseded",
  UNKNOWN: "Unknown",
};

export function FreshnessBadge({ hit }: { hit: EvidenceHit }) {
  const current = isPresentedAsCurrent(hit);
  const suffix = hit.freshness === "CURRENT" && !current ? " (not eligible)" : "";
  return (
    <span className={`tag es-fresh ${current ? "es-fresh-current" : "es-fresh-caution"}`} data-freshness={hit.freshness}>
      {FRESHNESS_TEXT[hit.freshness]}
      {suffix}
    </span>
  );
}

type ResultsProps = {
  view: SearchView;
  selectedRef: string | null;
  onSelect: (hit: EvidenceHit) => void;
  onMore: () => void;
  signInHref: string;
};

export function EvidenceResults({ view, selectedRef, onSelect, onMore, signInHref }: ResultsProps) {
  if (view.kind === "idle") {
    return <p className="empty-card">Enter a query to search verified evidence for your organization.</p>;
  }
  if (view.kind === "loading") {
    return (
      <p className="notice-card" role="status">
        Searching verified evidence…
      </p>
    );
  }
  if (view.kind === "error") {
    return (
      <div className="notice-card notice-danger" role="alert" data-failure={view.failure.kind}>
        <p>{view.failure.message}</p>
        {view.failure.kind === "unauthenticated" ? <a href={signInHref}>Sign in</a> : null}
      </div>
    );
  }
  if (view.hits.length === 0 && !view.nextCursor) {
    return (
      <p className="empty-card" role="status">
        No verified evidence matched. Nothing is shown from unverified or other-organization sources.
      </p>
    );
  }
  return (
    <div>
      {view.hits.length === 0 ? (
        <p className="empty-card" role="status">
          No verified evidence in the portion searched so far. More may exist: load more results to continue.
        </p>
      ) : null}
      <ol className="es-list" aria-label="Search results">
        {view.hits.map((hit) => (
          <li key={hit.evidenceRef} className="panel es-hit">
            <div className="es-hit-head">
              <FreshnessBadge hit={hit} />
              <span className="es-asof">Retrieved {formatInstant(hit.retrievedAtUtc)}</span>
            </div>
            <p className="es-ref">{hit.evidenceRef}</p>
            <p className="es-meta">
              {hit.jurisdiction} · {hit.sourceType} · attestation {hit.attestationState}
            </p>
            <button
              type="button"
              onClick={() => onSelect(hit)}
              aria-pressed={selectedRef === hit.evidenceRef}
              aria-label={`Inspect citation for ${hit.evidenceRef}`}
            >
              Inspect citation
            </button>
          </li>
        ))}
      </ol>
      {view.moreFailure ? (
        <div className="notice-card notice-danger" role="alert">
          {view.moreFailure.message}
        </div>
      ) : null}
      {view.nextCursor ? (
        <button type="button" onClick={onMore} disabled={view.loadingMore}>
          {view.loadingMore ? "Loading more…" : "Load more results"}
        </button>
      ) : null}
    </div>
  );
}

export function CitationInspector({ hit }: { hit: EvidenceHit | null }) {
  if (!hit) {
    return (
      <section className="panel es-inspector" aria-labelledby="es-inspector-title">
        <h2 id="es-inspector-title" className="panel-title">
          Citation inspector
        </h2>
        <p className="es-meta">Select a result to see where it comes from.</p>
      </section>
    );
  }
  const rows: [string, string][] = [
    ["Evidence reference", hit.evidenceRef],
    ["Source URI", hit.sourceUri],
    ["Source entry", hit.sourceEntryId],
    ["Jurisdiction", hit.jurisdiction],
    ["Source type", hit.sourceType],
    ["Attestation state", hit.attestationState],
    ["Retrieved (UTC)", formatInstant(hit.retrievedAtUtc)],
    ["Freshness evaluated (UTC)", formatInstant(hit.freshnessEvaluatedAt)],
    ["Eligible as current", hit.freshnessEligible ? "yes" : "no"],
    ["Freshness reason", hit.freshnessReason ?? "none given"],
    ["Parser name", hit.parserName ?? "not recorded"],
    ["Parser version", hit.parserVersion ?? "not recorded"],
  ];
  return (
    <section className="panel es-inspector" aria-labelledby="es-inspector-title">
      <h2 id="es-inspector-title" className="panel-title">
        Citation inspector
      </h2>
      <p>
        <FreshnessBadge hit={hit} />
      </p>
      <dl className="kv-grid">
        {rows.map(([label, value]) => (
          <div className="kv-row" key={label}>
            <dt className="kv-label">{label}</dt>
            <dd className="kv-value">{value}</dd>
          </div>
        ))}
      </dl>
    </section>
  );
}
