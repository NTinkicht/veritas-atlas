import { describeBlocker, type ComposeFailure, type ComposeResult } from "../api/answerReport";

// All API text (claim text, refs, codes) is rendered as React text children, never as HTML.

export type ReportView =
  | { kind: "idle" }
  | { kind: "loading" }
  | { kind: "error"; failure: ComposeFailure }
  | { kind: "loaded"; result: ComposeResult };

export function ReportResult({ view, signInHref }: { view: ReportView; signInHref: string }) {
  if (view.kind === "idle") return null;
  if (view.kind === "loading") return <p className="notice-card" role="status">Composing report...</p>;
  if (view.kind === "error") {
    return (
      <p className="notice-card notice-danger" role="alert">
        {view.failure.message}
        {view.failure.kind === "unauthenticated" ? <> <a href={signInHref}>Sign in</a></> : null}
      </p>
    );
  }
  const { result } = view;
  if (result.decision === "block") {
    return (
      <section className="panel notice-danger" aria-label="Report blocked" role="alert">
        <h2>Report blocked</h2>
        <p>The answer cannot be served. No claim text is shown for a blocked answer.</p>
        <ul aria-label="Blockers">
          {result.blockers.map((b, i) => (
            <li key={`${b.code}-${i}`}>
              {describeBlocker(b.code)} <span className="muted">({b.code}{b.claimId ? `, claim ${b.claimId}` : ""})</span>
            </li>
          ))}
        </ul>
      </section>
    );
  }
  const { claim } = result;
  return (
    <section className="panel" aria-label={`Report for ${claim.claimId}`}>
      <h2>Source-backed report</h2>
      <p className="muted">Human verification: {result.humanVerification} (asserted by the caller, not recorded by the server)</p>
      <p>{claim.text}</p>
      <p className="muted">Scope: {claim.temporalScope}</p>
      {claim.nonCurrentEvidence ? (
        <p className="notice-card" role="note">
          Historical evidence: this claim rests on non-current evidence, as of {claim.historicalAsOf}. It is not a current-state assertion.
        </p>
      ) : null}
      <ul aria-label="Citations">
        {claim.citations.map((c) => (
          <li key={c.evidenceRef}>
            <code>{c.evidenceRef}</code> <span className="muted">freshness: {c.freshness}</span>
          </li>
        ))}
      </ul>
      <p className="muted">
        Evidence and freshness were resolved by the server at serve time. This view does not verify anything itself.
      </p>
    </section>
  );
}
