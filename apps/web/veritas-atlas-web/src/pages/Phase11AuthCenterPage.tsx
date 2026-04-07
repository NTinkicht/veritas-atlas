import { Link } from "react-router-dom";

export function Phase11AuthCenterPage() {
  return (
    <div style={{ fontFamily: "Arial, sans-serif", padding: 24 }}>
      <h1 style={{ marginTop: 0 }}>Phase 11 Auth Center</h1>
      <p style={{ color: "#555" }}>
        Authentication and authorization hub for Veritas Atlas.
      </p>

      <div style={gridStyle}>
        <Link to="/login" style={cardStyle}>Login</Link>
        <Link to="/auth-me" style={cardStyle}>Current User</Link>
        <Link to="/role-policy" style={cardStyle}>Role Policy</Link>
        <Link to="/phase-10-hardening-center" style={cardStyle}>Phase 10 Hardening</Link>
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