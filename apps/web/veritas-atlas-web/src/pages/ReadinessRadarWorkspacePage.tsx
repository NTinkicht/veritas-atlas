import { Link } from "react-router-dom";
import { useClaims } from "../hooks/useClaims";
import { useContradictions } from "../hooks/useContradictions";
import { ReadinessRadarPanel } from "../components/ReadinessRadarPanel";

export function ReadinessRadarWorkspacePage() {
  const claimsQuery = useClaims();
  const contradictionsQuery = useContradictions();

  const items = [
    { label: "Claim review coverage", score: claimsQuery.data?.items.length ? 78 : 0 },
    { label: "Contradiction resolution", score: contradictionsQuery.data?.items.length ? 64 : 0 },
    { label: "Publication readiness", score: 55 },
    { label: "Decision traceability", score: 72 },
  ];

  return (
    <div style={{ fontFamily: "Arial, sans-serif", padding: 24 }}>
      <header style={{ marginBottom: 24 }}>
        <h1 style={{ margin: 0 }}>Readiness Radar Workspace</h1>
        <p style={{ color: "#555" }}>
          Operational readiness view across claims, contradictions, decision, and publication stages.
        </p>
        <nav style={{ display: "flex", gap: 16, marginTop: 12, flexWrap: "wrap" }}>
          <Link to="/readiness-radar-workspace">Readiness Radar</Link>
          <Link to="/decision-intelligence">Decision Intelligence</Link>
          <Link to="/publication-governance">Publication Governance</Link>
        </nav>
      </header>

      <ReadinessRadarPanel items={items} />
    </div>
  );
}