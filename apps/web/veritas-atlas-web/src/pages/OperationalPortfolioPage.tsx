import { Link } from "react-router-dom";
import { useCases } from "../hooks/useCases";
import { useClaims } from "../hooks/useClaims";
import { useContradictions } from "../hooks/useContradictions";
import { PortfolioRollupPanel } from "../components/PortfolioRollupPanel";

export function OperationalPortfolioPage() {
  const casesQuery = useCases();
  const claimsQuery = useClaims();
  const contradictionsQuery = useContradictions();

  const metrics = [
    { label: "Cases", value: casesQuery.data?.items.length ?? 0 },
    { label: "Claims", value: claimsQuery.data?.items.length ?? 0 },
    { label: "Contradictions", value: contradictionsQuery.data?.items.length ?? 0 },
    { label: "Open Review Work", value: contradictionsQuery.data?.items.filter(x => x.status !== "Resolved").length ?? 0 },
  ];

  return (
    <div style={{ fontFamily: "Arial, sans-serif", padding: 24 }}>
      <header style={{ marginBottom: 24 }}>
        <h1 style={{ margin: 0 }}>Operational Portfolio</h1>
        <p style={{ color: "#555" }}>
          Rollup across the major operational entities in the platform.
        </p>
        <nav style={{ display: "flex", gap: 16, marginTop: 12, flexWrap: "wrap" }}>
          <Link to="/operational-portfolio">Operational Portfolio</Link>
          <Link to="/case-explorer">Case Explorer</Link>
          <Link to="/knowledge-graph-hub">Knowledge Graph Hub</Link>
        </nav>
      </header>

      <PortfolioRollupPanel metrics={metrics} />
    </div>
  );
}