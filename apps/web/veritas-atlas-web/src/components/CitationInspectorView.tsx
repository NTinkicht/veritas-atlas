import { describeCitationCode, type CitationFailure, type CitationVerdict } from "../api/citationInspector";

// All API text (references, codes, dates) is rendered as React text children, never as HTML.

export type CitationView =
  | { kind: "idle" }
  | { kind: "loading" }
  | { kind: "error"; failure: CitationFailure }
  | { kind: "loaded"; verdict: CitationVerdict };

export function CitationResult({ view, signInHref }: { view: CitationView; signInHref: string }) {
  if (view.kind === "idle") return null;
  if (view.kind === "loading") return <p className="notice-card" role="status">Inspecting citations...</p>;
  if (view.kind === "error") {
    return (
      <p className="notice-card notice-danger" role="alert">
        {view.failure.message}
        {view.failure.kind === "unauthenticated" ? <> <a href={signInHref}>Sign in</a></> : null}
      </p>
    );
  }
  const { verdict } = view;
  return (
    <section className="panel" aria-label="Citation verdict">
      <h2>{verdict.valid ? "Citations accepted" : "Citations rejected"}</h2>
      {verdict.code ? <p role="alert">{describeCitationCode(verdict.code)}</p> : null}
      <ul aria-label="Cited evidence">
        {verdict.items.map((item, i) => (
          <li key={`${i}:${item.evidenceRef}`}>
            <strong>{item.valid ? "Valid" : "Rejected"}</strong> <code>{item.evidenceRef}</code>
            {item.code ? <> - {describeCitationCode(item.code)}</> : null}
            {item.freshness ? <> Freshness: {item.freshness}.</> : null}
            {item.evaluatedAt ? <> Evaluated at {item.evaluatedAt}.</> : null}
            {item.historicalAsOf ? <> Historical as of {item.historicalAsOf}; this is not a current-state assertion.</> : null}
          </li>
        ))}
      </ul>
      <p className="muted">This is the server&apos;s verdict for the references you entered. Nothing was repaired, refreshed or substituted.</p>
    </section>
  );
}
