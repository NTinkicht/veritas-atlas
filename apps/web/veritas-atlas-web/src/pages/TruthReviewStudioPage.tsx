import { Link } from "react-router-dom";
import { useClaims } from "../hooks/useClaims";
import { useContradictions } from "../hooks/useContradictions";
import { TruthMatrixPanel } from "../components/TruthMatrixPanel";

export function TruthReviewStudioPage() {
  const claimsQuery = useClaims();
  const contradictionsQuery = useContradictions();

  const claimItems = (claimsQuery.data?.items ?? []).slice(0, 8).map((item) => ({
    id: item.id,
    label: item.topic,
    status: item.status,
  }));

  const contradictionItems = (contradictionsQuery.data?.items ?? []).slice(0, 8).map((item) => ({
    id: item.id,
    label: item.topic,
    status: item.status,
  }));

  return (
    <div style={{ fontFamily: "Arial, sans-serif", padding: 24 }}>
      <header style={{ marginBottom: 24 }}>
        <h1 style={{ margin: 0 }}>Truth Review Studio</h1>
        <p style={{ color: "#555" }}>
          Unified review surface for claims, contradictions, and resolution readiness.
        </p>

        <nav style={{ display: "flex", gap: 16, marginTop: 12, flexWrap: "wrap" }}>
          <Link to="/">Home</Link>
          <Link to="/case-explorer">Case Explorer</Link>
          <Link to="/truth-review-studio">Truth Review Studio</Link>
          <Link to="/resolution-board">Resolution Board</Link>
          <Link to="/publication-desk">Publication Desk</Link>
        </nav>
      </header>

      <TruthMatrixPanel claims={claimItems} contradictions={contradictionItems} />

      <div style={{ ...panelStyle, marginTop: 20 }}>
        <h3 style={{ marginTop: 0 }}>Studio Summary</h3>
        <ul style={{ marginBottom: 0 }}>
          <li>Total visible claims: {claimsQuery.data?.items.length ?? 0}</li>
          <li>Total visible contradictions: {contradictionsQuery.data?.items.length ?? 0}</li>
          <li>Ready for deeper resolution workflow: yes</li>
        </ul>
      </div>
    </div>
  );
}

const panelStyle: React.CSSProperties = {
  border: "1px solid #ddd",
  borderRadius: 14,
  padding: 16,
};