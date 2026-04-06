import { Link } from "react-router-dom";
import { useClaims } from "../hooks/useClaims";
import { useContradictions } from "../hooks/useContradictions";
import { EntityGraphPanel } from "../components/EntityGraphPanel";

export function EntityGraphWorkspacePage() {
  const claimsQuery = useClaims();
  const contradictionsQuery = useContradictions();

  const claimNodes = (claimsQuery.data?.items ?? []).slice(0, 6).map((item) => ({
    id: item.id,
    label: item.topic,
    type: "Claim",
  }));

  const contradictionNodes = (contradictionsQuery.data?.items ?? []).slice(0, 6).map((item) => ({
    id: item.id,
    label: item.topic,
    type: "Contradiction",
  }));

  const nodes = [...claimNodes, ...contradictionNodes];

  return (
    <div style={{ fontFamily: "Arial, sans-serif", padding: 24 }}>
      <header style={{ marginBottom: 24 }}>
        <h1 style={{ margin: 0 }}>Entity Graph Workspace</h1>
        <p style={{ color: "#555" }}>
          Relationship-oriented operational surface for claims and contradictions.
        </p>
        <nav style={{ display: "flex", gap: 16, marginTop: 12, flexWrap: "wrap" }}>
          <Link to="/entity-graph">Entity Graph</Link>
          <Link to="/case-explorer">Case Explorer</Link>
          <Link to="/truth-review-studio">Truth Review Studio</Link>
        </nav>
      </header>

      <EntityGraphPanel nodes={nodes} />
    </div>
  );
}