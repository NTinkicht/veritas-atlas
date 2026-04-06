import { Link, useSearchParams } from "react-router-dom";

export function ClaimsWorkspacePage() {
  const [params] = useSearchParams();
  const statementId = params.get("statementId") ?? "";

  return (
    <div style={{ fontFamily: "Arial, sans-serif", padding: "24px", maxWidth: "900px" }}>
      <header style={{ marginBottom: "24px" }}>
        <h1 style={{ margin: 0 }}>Claims Workspace</h1>
        <p style={{ color: "#555" }}>Lightweight workspace placeholder for the next claim slice.</p>
        <nav style={{ display: "flex", gap: "16px", marginTop: "12px", flexWrap: "wrap" }}>
          <Link to="/">Home</Link>
          <Link to="/statements">Statements</Link>
          {statementId && <Link to={`/statements/${statementId}`}>Statement</Link>}
        </nav>
      </header>

      <div style={cardStyle}>
        <Row label="Statement Id" value={statementId || "N/A"} />
        <Row label="Status" value="Placeholder ready" />
        <Row label="Next use" value="Claim create/list/detail can be added here next." />
      </div>

      <div style={cardStyle}>
        <h3 style={{ marginTop: 0 }}>Planned actions</h3>
        <ul style={{ marginBottom: 0 }}>
          <li>Create claim from current statement</li>
          <li>List claims linked to statement</li>
          <li>Navigate to contradiction workflows later</li>
        </ul>
      </div>
    </div>
  );
}

function Row({ label, value }: { label: string; value: string }) {
  return (
    <div style={{ display: "grid", gridTemplateColumns: "180px 1fr", gap: "12px", padding: "6px 0" }}>
      <strong>{label}</strong>
      <span>{value}</span>
    </div>
  );
}

const cardStyle: React.CSSProperties = {
  border: "1px solid #ddd",
  borderRadius: "12px",
  padding: "16px",
  marginBottom: "16px",
};
