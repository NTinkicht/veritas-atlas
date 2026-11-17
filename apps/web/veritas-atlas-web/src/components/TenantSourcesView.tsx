import type { TenantSource, TenantSourcesFailure } from "../api/tenantSources";

// All API text is rendered as React text children, never as HTML.

export type SourcesView =
  | { kind: "loading" }
  | { kind: "error"; failure: TenantSourcesFailure }
  | { kind: "loaded"; sources: TenantSource[] };

export function TenantSourcesListView({
  view,
  signInHref,
  onViewAttempts,
}: {
  view: SourcesView;
  signInHref: string;
  onViewAttempts?: (source: TenantSource) => void;
}) {
  if (view.kind === "loading") return <p className="notice-card" role="status">Loading sources...</p>;
  if (view.kind === "error") {
    return (
      <p className="notice-card notice-danger" role="alert">
        {view.failure.message}
        {view.failure.kind === "unauthenticated" ? <> <a href={signInHref}>Sign in</a></> : null}
      </p>
    );
  }
  if (view.sources.length === 0) {
    return <p className="notice-card" role="status">No sources are registered for your organization yet.</p>;
  }
  return (
    <section className="panel" aria-label="Registered sources">
      <h2>Registered sources ({view.sources.length})</h2>
      <ul>
        {view.sources.map((source) => (
          <li key={source.id}>
            <code id={`source-key-${source.id}`}>{source.canonicalKey}</code>
            <span className="muted"> registered <time dateTime={source.createdAtUtc}>{source.createdAtUtc}</time></span>
            {onViewAttempts ? (
              <> <button type="button" aria-describedby={`source-key-${source.id}`} onClick={() => onViewAttempts(source)}>View attempts</button></>
            ) : null}
          </li>
        ))}
      </ul>
      <p className="notice-card" role="note">
        This is the registry only. Registration is an administrator action, and a listed source is not proof that any record from it is verified.
      </p>
    </section>
  );
}
