import type { CandidatesFailure, ClaimCandidatesResult } from "../api/claimCandidates";

// All API text is rendered as React text children, never as HTML.

export type CandidatesView =
  | { kind: "idle" }
  | { kind: "loading" }
  | { kind: "error"; failure: CandidatesFailure }
  | { kind: "loaded"; result: ClaimCandidatesResult };

export function ClaimCandidatesResultView({ view, signInHref }: { view: CandidatesView; signInHref: string }) {
  if (view.kind === "idle") return null;
  if (view.kind === "loading") return <p className="notice-card" role="status">Listing claim candidates...</p>;
  if (view.kind === "error") {
    return (
      <p className="notice-card notice-danger" role="alert">
        {view.failure.message}
        {view.failure.kind === "unauthenticated" ? <> <a href={signInHref}>Sign in</a></> : null}
      </p>
    );
  }
  const { result } = view;
  return (
    <section className="panel" aria-label="Claim candidates">
      <h2>Claim candidates</h2>
      <p className="muted">Evidence: <code>{result.evidenceRef}</code></p>
      <p className="muted">Freshness: {result.freshness.state}{result.freshness.eligible ? "" : " (not eligible as current-state evidence)"}</p>
      {result.claims.length === 0 ? (
        <p>No claim candidates were found in this evidence.</p>
      ) : (
        <ol>
          {result.claims.map((claim) => (
            <li key={claim.claimKey}>
              <blockquote>{claim.quotation}</blockquote>
              <p className="muted">
                Unconfirmed, not answerable. Element {claim.elementId}, characters {claim.start} to {claim.start + claim.length}.
              </p>
            </li>
          ))}
        </ol>
      )}
      <p className="notice-card" role="note">
        These are candidate quotations, not claims. None is confirmed, answerable or publishable. Extractor {result.extractorName} {result.extractorVersion}.
      </p>
    </section>
  );
}
