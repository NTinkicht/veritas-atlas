import { Link } from "react-router-dom";

export function Phase12DataCenterPage() {
  return (
    <div style={{ fontFamily: "Arial, sans-serif", padding: 24 }}>
      <h1 style={{ marginTop: 0 }}>Phase 12 Data Center</h1>
      <p style={{ color: "#555" }}>
        Data persistence, reset tooling, diagnostics, and integration verification hub.
      </p>

      <div style={gridStyle}>
        <Link to="/persistence-console" style={cardStyle}>Persistence Console</Link>
        <Link to="/lifecycle-seed" style={cardStyle}>Lifecycle Seed</Link>
        <Link to="/seeded-lifecycle-runner" style={cardStyle}>Seeded Lifecycle Runner</Link>
        <Link to="/workflow-audit" style={cardStyle}>Workflow Audit</Link>
        <Link to="/integration-test-center" style={cardStyle}>Integration Test Center</Link>
        <Link to="/phase-11-auth-center" style={cardStyle}>Phase 11 Auth</Link>
      </div>
    </div>
  );
}

const gridStyle: React.CSSProperties = {
  display: "grid",
  gridTemplateColumns: "repeat(auto-fit, minmax(220px, 1fr))",
  gap: 16,
};

const cardStyle: React.CSSProperties = {
  border: "1px solid #ddd",
  borderRadius: 14,
  padding: 16,
  textDecoration: "none",
  color: "inherit",
};