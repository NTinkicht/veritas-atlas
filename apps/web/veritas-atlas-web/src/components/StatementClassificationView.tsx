import type { ClassificationResult, ClassifyFailure, StatementClass } from "../api/statementClassification";

// All API text is rendered as React text children, never as HTML. The submitted statement is never shown here.

export type ClassificationView =
  | { kind: "idle" }
  | { kind: "loading" }
  | { kind: "error"; failure: ClassifyFailure }
  | { kind: "loaded"; result: ClassificationResult };

const CLASS_TEXT: Record<StatementClass, string> = {
  quotation: "Quotation: the statement appears verbatim in the evidence.",
  inference: "Inference: the statement goes beyond the evidence wording.",
  unresolved: "Unresolved: the evidence does not settle the statement.",
  unsupported: "Unsupported: no verified evidence supports the statement.",
};

export function ClassificationResultView({ view, signInHref }: { view: ClassificationView; signInHref: string }) {
  if (view.kind === "idle") return null;
  if (view.kind === "loading") return <p className="notice-card" role="status">Classifying statement...</p>;
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
    <section className="panel" aria-label="Statement classification">
      <h2>Statement classification</h2>
      <p>{CLASS_TEXT[result.statementClass]}</p>
      <p className="muted">Evidence: <code>{result.evidenceRef}</code></p>
      {result.supportingEvidenceRef ? <p className="muted">Supporting evidence: <code>{result.supportingEvidenceRef}</code></p> : null}
      {result.span ? (
        <p className="muted">Matched element {result.span.elementId}, characters {result.span.start} to {result.span.start + result.span.length}.</p>
      ) : null}
      <p className="muted">Freshness: {result.freshness.state}{result.freshness.eligible ? "" : " (not eligible as current-state evidence)"}</p>
      <p className="notice-card" role="note">
        This is a classification, not a claim. It is never answerable and cannot be published. Classifier {result.classifierName} {result.classifierVersion}.
      </p>
    </section>
  );
}
