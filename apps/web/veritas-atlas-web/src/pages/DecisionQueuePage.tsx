import { Link } from "react-router-dom";
import { useClaims } from "../hooks/useClaims";

export function DecisionQueuePage() {
  const claimsQuery = useClaims();
  const claims = claimsQuery.data?.items ?? [];

  return (
    <div style={{ fontFamily: "Arial, sans-serif", padding: 24 }}>
      <header style={{ marginBottom: 24 }}>
        <h1 style={{ margin: 0 }}>Decision Queue</h1>
        <p style={{ color: "#555" }}>
          Queue view for items approaching final decision and publication readiness.
        </p>

        <nav style={{ display: "flex", gap: 16, marginTop: 12, flexWrap: "wrap" }}>
          <Link to="/decision-queue">Decision Queue</Link>
          <Link to="/decision-intelligence">Decision Intelligence</Link>
          <Link to="/publication-pipeline">Publication Pipeline</Link>
        </nav>
      </header>

      <div style={panelStyle}>
        <h3 style={{ marginTop: 0 }}>Queued Items</h3>
        {claims.length === 0 && <p>No queued items.</p>}
        {claims.length > 0 && (
          <ul style={{ marginBottom: 0 }}>
            {claims.slice(0, 12).map((item) => (
              <li key={item.id}>
                <Link to={`/claims/${item.id}`}>{item.topic}</Link> - {item.status}
              </li>
            ))}
          </ul>
        )}
      </div>
    </div>
  );
}

const panelStyle: React.CSSProperties = {
  border: "1px solid #ddd",
  borderRadius: 14,
  padding: 16,
};