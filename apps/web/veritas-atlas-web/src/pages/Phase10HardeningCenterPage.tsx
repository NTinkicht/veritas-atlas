import { Link } from "react-router-dom";

export function Phase10HardeningCenterPage() {
  return (
    <div style={{ fontFamily: "Arial, sans-serif", padding: 24 }}>
      <h1 style={{ marginTop: 0 }}>Phase 10 Hardening Center</h1>
      <p style={{ color: "#555" }}>
        Production-readiness hub for authorization, persistent audit, workflow integrity, and verification.
      </p>

      <div style={gridStyle}>
        <Link to="/workflow-audit" style={cardStyle}>Workflow Audit</Link>
        <Link to="/audit-persistence" style={cardStyle}>Audit Persistence</Link>
        <Link to="/role-policy" style={cardStyle}>Role Policy</Link>
        <Link to="/workflow-validation" style={cardStyle}>Workflow Validation</Link>
        <Link to="/phase-9-integrity-center" style={cardStyle}>Phase 9 Integrity</Link>
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