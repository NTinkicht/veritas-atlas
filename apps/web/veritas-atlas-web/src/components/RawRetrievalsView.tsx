import type { RawRetrieval, RawRetrievalsFailure } from "../api/tenantRawRetrievals";

// All API text is rendered as React text children, never as HTML.

export type RetrievalsView =
  | { kind: "idle" }
  | { kind: "loading"; sourceKey: string }
  | { kind: "error"; sourceKey: string; failure: RawRetrievalsFailure }
  | { kind: "loaded"; sourceKey: string; retrievals: RawRetrieval[] };

export function RawRetrievalsPanel({ view, signInHref }: { view: RetrievalsView; signInHref: string }) {
  if (view.kind === "idle") return null;
  if (view.kind === "loading") return <p className="notice-card" role="status">Loading raw retrievals...</p>;
  if (view.kind === "error") {
    return (
      <p className="notice-card notice-danger" role="alert">
        {view.failure.message}
        {view.failure.kind === "unauthenticated" ? <> <a href={signInHref}>Sign in</a></> : null}
      </p>
    );
  }
  return (
    <section className="panel" aria-label="Raw retrievals">
      <h2>Raw retrievals for <code>{view.sourceKey}</code> ({view.retrievals.length})</h2>
      {view.retrievals.length === 0 ? (
        <p role="status">No raw retrievals are recorded for this source.</p>
      ) : (
        <ul>
          {view.retrievals.map((r) => (
            <li key={r.id}>
              <code>{r.finalUri}</code>{" "}
              <time dateTime={r.retrievedAtUtc}>{r.retrievedAtUtc}</time>{" "}
              <span>{r.byteLength} bytes</span>{" "}
              <span className="muted">sha256 <code>{r.contentSha256}</code></span>
            </li>
          ))}
        </ul>
      )}
      <p className="notice-card" role="note">
        Read-only metadata of the newest stored retrievals. A raw retrieval is unparsed and is not verified evidence or a citation.
      </p>
    </section>
  );
}
