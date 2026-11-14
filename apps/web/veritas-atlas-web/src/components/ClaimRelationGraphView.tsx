import { describeReason, type GraphFailure, type RelationGraph } from "../api/claimRelationGraph";

// All API text (ids, evidence references, reason codes) is rendered as React text children, never as HTML.

export type GraphView =
  | { kind: "idle" }
  | { kind: "loading" }
  | { kind: "error"; failure: GraphFailure }
  | { kind: "loaded"; graph: RelationGraph };

const RELATION_LABEL = { agreeing: "Agreeing", conflicting: "Conflicting", superseded: "Superseded", unresolved: "Unresolved" } as const;
const STATUS_LABEL = { conflicted: "Conflicted", unresolved: "Unresolved", superseded: "Superseded", agreeing: "Agreeing", isolated: "No relations" } as const;

export function GraphResult({ view, signInHref }: { view: GraphView; signInHref: string }) {
  if (view.kind === "idle") return null;
  if (view.kind === "loading") return <p className="notice-card" role="status">Comparing claims...</p>;
  if (view.kind === "error") {
    return (
      <p className="notice-card notice-danger" role="alert">
        {view.failure.message}
        {view.failure.kind === "unauthenticated" ? <> <a href={signInHref}>Sign in</a></> : null}
      </p>
    );
  }
  const { nodes, edges } = view.graph;
  return (
    <section className="panel" aria-label="Claim relations">
      <h2>Claims ({nodes.length})</h2>
      <ul aria-label="Claim statuses">
        {nodes.map((n) => (
          <li key={n.claimId}>
            {n.claimId}: {STATUS_LABEL[n.status]} (evidence {n.evidenceRef}{n.sourceId ? `, source ${n.sourceId}` : ""})
          </li>
        ))}
      </ul>
      <h2>Relations ({edges.length})</h2>
      {edges.length === 0 ? <p>No related claims were found.</p> : null}
      <ul aria-label="Claim relations list">
        {edges.map((e) => (
          <li key={`${e.firstClaimId}|${e.secondClaimId}`}>
            <strong>{RELATION_LABEL[e.relation]}</strong>: {e.firstClaimId} (evidence {e.firstEvidenceRef}
            {e.firstSourceId ? `, source ${e.firstSourceId}` : ""}) and {e.secondClaimId} (evidence {e.secondEvidenceRef}
            {e.secondSourceId ? `, source ${e.secondSourceId}` : ""}). {describeReason(e.reason)}
            {e.supersededClaimId ? ` ${e.supersededClaimId} is superseded by ${e.supersedingClaimId ?? "a later claim"}.` : ""}
          </li>
        ))}
      </ul>
      <p className="muted">
        Evidence references are echoed from your input and were not verified. These relations describe how the entered
        claims compare; they do not establish which claim is true.
      </p>
    </section>
  );
}
