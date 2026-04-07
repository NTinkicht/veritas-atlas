import { Link } from "react-router-dom";

export function Phase9IntegrityCenterPage() {
  return (
    <div style={{ fontFamily: "Arial, sans-serif", padding: 24 }}>
      <h1 style={{ marginTop: 0 }}>Phase 9 Integrity Center</h1>
      <p style={{ color: "#555" }}>
        Central surface for workflow integrity, validation, diagnostics, role testing, and seeded lifecycle execution.
      </p>

      <div style={gridStyle}>
        <Link to="/workflow-console" style={cardStyle}>Workflow Console</Link>
        <Link to="/workflow-diagnostics" style={cardStyle}>Workflow Diagnostics</Link>
        <Link to="/workflow-audit" style={cardStyle}>Workflow Audit</Link>
        <Link to="/workflow-validation" style={cardStyle}>Workflow Validation</Link>
        <Link to="/lifecycle-seed" style={cardStyle}>Lifecycle Seed</Link>
        <Link to="/seeded-lifecycle-runner" style={cardStyle}>Seeded Lifecycle Runner</Link>
        <Link to="/mutation-playground" style={cardStyle}>Mutation Playground</Link>
        <Link to="/integration-test-center" style={cardStyle}>Integration Test Center</Link>
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