import { describeReason, type ClaimConfidenceLabel, type ConfidenceFailure } from "../api/claimConfidence";

// All API text (claim id, reason codes) is rendered as React text children, never as HTML.

export type ConfidenceView =
  | { kind: "idle" }
  | { kind: "loading" }
  | { kind: "error"; failure: ConfidenceFailure }
  | { kind: "loaded"; label: ClaimConfidenceLabel };

export function ConfidenceResult({ view, signInHref }: { view: ConfidenceView; signInHref: string }) {
  if (view.kind === "idle") return null;
  if (view.kind === "loading") return <p className="notice-card" role="status">Labeling confidence...</p>;
  if (view.kind === "error") {
    return (
      <p className="notice-card notice-danger" role="alert">
        {view.failure.message}
        {view.failure.kind === "unauthenticated" ? <> <a href={signInHref}>Sign in</a></> : null}
      </p>
    );
  }
  const { label } = view;
  return (
    <section className="panel" aria-label={`Confidence for ${label.claimId}`}>
      <h2>Evidence confidence: {label.band}</h2>
      <p className="muted">Claim {label.claimId}</p>
      <ul aria-label="Reasons">
        {label.reasons.map((reason) => (
          <li key={reason}>{describeReason(reason)}</li>
        ))}
      </ul>
      <p className="muted">
        This band describes the quality of the supplied evidence signals only. It is not a judgement that the claim is true,
        and this check did not verify any evidence.
      </p>
    </section>
  );
}
