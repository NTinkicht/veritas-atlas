import { Link } from "react-router-dom";
import { useClaims } from "../hooks/useClaims";
import { useStatements } from "../hooks/useStatements";
import { useContradictions } from "../hooks/useContradictions";
import { KnowledgeGraphSummaryPanel } from "../components/KnowledgeGraphSummaryPanel";

export function KnowledgeGraphHubPage() {
  const claimsQuery = useClaims();
  const statementsQuery = useStatements();
  const contradictionsQuery = useContradictions();

  const metrics = [
    { label: "Claims", value: claimsQuery.data?.items.length ?? 0 },
    { label: "Statements", value: statementsQuery.data?.items.length ?? 0 },
    { label: "Contradictions", value: contradictionsQuery.data?.items.length ?? 0 },
    { label: "Links", value: (claimsQuery.data?.items.length ?? 0) + (contradictionsQuery.data?.items.length ?? 0) },
  ];

  return (
    <div style={{ fontFamily: "Arial, sans-serif", padding: 24 }}>
      <header style={{ marginBottom: 24 }}>
        <h1 style={{ margin: 0 }}>Knowledge Graph Hub</h1>
        <p style={{ color: "#555" }}>
          Relationship-centered operational view across statements, claims, contradictions, and graph density.
        </p>
        <nav style={{ display: "flex", gap: 16, marginTop: 12, flexWrap: "wrap" }}>
          <Link to="/">Home</Link>
          <Link to="/knowledge-graph-hub">Knowledge Graph Hub</Link>
          <Link to="/entity-graph">Entity Graph</Link>
          <Link to="/case-explorer">Case Explorer</Link>
        </nav>
      </header>

      <KnowledgeGraphSummaryPanel metrics={metrics} />
    </div>
  );
}