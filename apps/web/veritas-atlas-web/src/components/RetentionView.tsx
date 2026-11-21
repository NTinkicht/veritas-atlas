import { RETENTION_DENIAL_LABELS, type RetentionFailure, type RetentionView } from "../api/tenantRetention";

// All API text is rendered as React text children, never as HTML.

export type RetentionState =
  | { kind: "idle" }
  | { kind: "loading"; retrievalId: string }
  | { kind: "error"; retrievalId: string; failure: RetentionFailure }
  | { kind: "loaded"; retrievalId: string; view: RetentionView };

export function RetentionPanel({ state, signInHref }: { state: RetentionState; signInHref: string }) {
  if (state.kind === "idle") return null;
  if (state.kind === "loading") return <p className="notice-card" role="status">Loading retention...</p>;
  if (state.kind === "error") {
    return (
      <p className="notice-card notice-danger" role="alert">
        {state.failure.message}
        {state.failure.kind === "unauthenticated" ? <> <a href={signInHref}>Sign in</a></> : null}
      </p>
    );
  }
  const { eligibility: e, history } = state.view;
  return (
    <section className="panel" aria-label="Retention">
      <h3>Retention of <code>{state.retrievalId}</code></h3>
      <p>
        Dry run: <strong>{e.outcome === "Allow" ? "would be eligible for purge" : "not eligible for purge"}</strong>
        {e.denialCode ? <> ({RETENTION_DENIAL_LABELS[e.denialCode]})</> : null}
      </p>
      <p className="muted">
        {e.parseRunCount} parse run(s), {e.attestationCount} attestation(s), legal hold {e.legalHold ? "on" : "off"}, retain until{" "}
        {e.retainUntilUtc ? <time dateTime={e.retainUntilUtc}>{e.retainUntilUtc}</time> : "not recorded"}.
      </p>
      {history.length === 0 ? (
        <p role="status">No retention version is recorded for this retrieval.</p>
      ) : (
        <ol aria-label="Retention versions">
          {history.map((v) => (
            <li key={v.version}>
              v{v.version}: legal hold {v.legalHold ? "on" : "off"}, retain until {v.retainUntilUtc ?? "not recorded"}, by{" "}
              <span>{v.actorSubject}</span> at <time dateTime={v.createdAtUtc}>{v.createdAtUtc}</time>
            </li>
          ))}
        </ol>
      )}
      <p className="notice-card" role="note">
        Read-only. This dry run never deletes anything; raw bytes stay stored and are not verified evidence.
      </p>
    </section>
  );
}
