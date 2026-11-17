import type { FetchAttempt, FetchAttemptsFailure } from "../api/tenantFetchAttempts";

// All API text is rendered as React text children, never as HTML.

export type AttemptsView =
  | { kind: "idle" }
  | { kind: "loading"; sourceKey: string }
  | { kind: "error"; sourceKey: string; failure: FetchAttemptsFailure }
  | { kind: "loaded"; sourceKey: string; attempts: FetchAttempt[] };

export function FetchAttemptsPanel({ view, signInHref }: { view: AttemptsView; signInHref: string }) {
  if (view.kind === "idle") return null;
  if (view.kind === "loading") return <p className="notice-card" role="status">Loading fetch attempts...</p>;
  if (view.kind === "error") {
    return (
      <p className="notice-card notice-danger" role="alert">
        {view.failure.message}
        {view.failure.kind === "unauthenticated" ? <> <a href={signInHref}>Sign in</a></> : null}
      </p>
    );
  }
  return (
    <section className="panel" aria-label="Fetch attempts">
      <h2>Recent fetch attempts for <code>{view.sourceKey}</code> ({view.attempts.length})</h2>
      {view.attempts.length === 0 ? (
        <p role="status">No fetch attempts are recorded for this source.</p>
      ) : (
        <ul>
          {view.attempts.map((a) => (
            <li key={a.id}>
              <code>{a.requestedUri}</code>{" "}
              <time dateTime={a.attemptedAtUtc}>{a.attemptedAtUtc}</time>{" "}
              <strong>{a.outcome}</strong>
              {a.failureClass ? <span> ({a.failureClass})</span> : null}
              {a.httpStatus !== null ? <span> HTTP {a.httpStatus}</span> : null}
            </li>
          ))}
        </ul>
      )}
      <p className="notice-card" role="note">
        Read-only ledger of the newest attempts. A successful attempt is not proof that the retrieved record is parsed or verified.
      </p>
    </section>
  );
}
