import { formatInstant } from "../api/evidenceSearch";
import type { DetailFailure, EvidenceDetail } from "../api/evidenceDetail";
import { FreshnessBadge } from "./EvidenceSearchView";

// VER-P0-17 (#344). All server-provided text (URIs, chunk text, reasons) is rendered as React text
// children, never as HTML, and the source URI is shown as text, not as a link.

export type DetailView =
  | { kind: "loading" }
  | { kind: "error"; failure: DetailFailure }
  | { kind: "loaded"; detail: EvidenceDetail };

export function EvidenceDetailBody({ view, signInHref }: { view: DetailView; signInHref: string }) {
  if (view.kind === "loading") {
    return (
      <p className="notice-card" role="status">
        Loading verified evidence…
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
  const d = view.detail;
  const rows: [string, string][] = [
    ["Evidence reference", d.evidenceRef],
    ["Source URI", d.sourceUri],
    ["Source entry", d.sourceEntryId],
    ["Jurisdiction", d.jurisdiction],
    ["Source type", d.sourceType],
    ["Attestation state", d.attestationState],
    ["Retrieved (UTC)", formatInstant(d.retrievedAtUtc)],
    ["Freshness evaluated (UTC)", formatInstant(d.freshnessEvaluatedAt)],
    ["Eligible as current", d.freshnessEligible ? "yes" : "no"],
    ["Freshness reason", d.freshnessReason ?? "none given"],
    ["Parser name", d.parserName ?? "not recorded"],
    ["Parser version", d.parserVersion ?? "not recorded"],
  ];
  return (
    <div>
      <section className="panel" aria-labelledby="ed-prov-title">
        <h2 id="ed-prov-title" className="panel-title">
          Provenance
        </h2>
        <p>
          <FreshnessBadge hit={d} />
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
      <section className="panel" aria-labelledby="ed-chunks-title">
        <h2 id="ed-chunks-title" className="panel-title">
          Verified text
        </h2>
        {d.chunksTruncated ? (
          <p className="notice-card" role="status">
            Showing a bounded portion of this record: more text exists than is displayed here.
          </p>
        ) : null}
        {d.chunks.length === 0 ? (
          <p className="empty-card">No verified text chunks are recorded for this evidence.</p>
        ) : (
          <ol className="es-list" aria-label="Verified text chunks">
            {d.chunks.map((chunk) => (
              <li key={`${chunk.ordinal}-${chunk.elementId}`} className="es-chunk">
                <p className="es-meta">
                  Chunk {chunk.ordinal} · {chunk.elementId}
                </p>
                <p className="es-chunk-text">{chunk.text}</p>
              </li>
            ))}
          </ol>
        )}
      </section>
    </div>
  );
}
