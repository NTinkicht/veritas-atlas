import { Link } from "react-router-dom";
import { useClaims } from "../hooks/useClaims";
import { useContradictions } from "../hooks/useContradictions";

export function OperationalHandoffPage() {
  const claimsQuery = useClaims();
  const contradictionsQuery = useContradictions();

  const claims = claimsQuery.data?.items ?? [];
  const contradictions = contradictionsQuery.data?.items ?? [];

  return (
    <div style={{ fontFamily: "Arial, sans-serif", padding: 24 }}>
      <header style={{ marginBottom: 24 }}>
        <h1 style={{ margin: 0 }}>Operational Handoff</h1>
        <p style={{ color: "#555" }}>
          Cross-workspace handoff view between review, contradiction resolution, and publication preparation.
        </p>

        <nav style={{ display: "flex", gap: 16, marginTop: 12, flexWrap: "wrap" }}>
          <Link to="/operational-handoff">Operational Handoff</Link>
          <Link to="/review-decision-board">Review Decision Board</Link>
          <Link to="/publication-pipeline">Publication Pipeline</Link>
        </nav>
      </header>

      <div style={gridStyle}>
        <div style={panelStyle}>
          <h3 style={{ marginTop: 0 }}>Claims Ready For Review</h3>
          {claims.length === 0 && <p>No claims.</p>}
          {claims.length > 0 && (
            <ul style={{ marginBottom: 0 }}>
              {claims.slice(0, 8).map((item) => (
                <li key={item.id}>
                  <Link to={`/claims/${item.id}`}>{item.topic}</Link> - {item.status}
                </li>
              ))}
            </ul>
          )}
        </div>

        <div style={panelStyle}>
          <h3 style={{ marginTop: 0 }}>Contradictions Ready For Resolution</h3>
          {contradictions.length === 0 && <p>No contradictions.</p>}
          {contradictions.length > 0 && (
            <ul style={{ marginBottom: 0 }}>
              {contradictions.slice(0, 8).map((item) => (
                <li key={item.id}>
                  <Link to={`/contradiction-resolution/${item.id}`}>{item.topic}</Link> - {item.status}
                </li>
              ))}
            </ul>
          )}
        </div>
      </div>
    </div>
  );
}

const gridStyle: React.CSSProperties = {
  display: "grid",
  gridTemplateColumns: "1fr 1fr",
  gap: 16,
};

const panelStyle: React.CSSProperties = {
  border: "1px solid #ddd",
  borderRadius: 14,
  padding: 16,
};