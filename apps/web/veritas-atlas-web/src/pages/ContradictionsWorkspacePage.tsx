import { Link, useSearchParams } from "react-router-dom";
import { useClaimDetail } from "../hooks/useClaimDetail";
import { useStatementDetail } from "../hooks/useStatementDetail";

export function ContradictionsWorkspacePage() {
  const [params] = useSearchParams();
  const claimId = params.get("claimId") ?? "";
  const statementId = params.get("statementId") ?? "";

  const claimQuery = useClaimDetail(claimId || undefined);
  const statementQuery = useStatementDetail(statementId || undefined);

  return (
    <div style={{ fontFamily: "Arial, sans-serif", padding: "24px", maxWidth: "980px" }}>
      <header style={{ marginBottom: "24px" }}>
        <h1 style={{ margin: 0 }}>Contradictions Workspace</h1>
        <p style={{ color: "#555" }}>Operational preparation space for the contradiction slice.</p>
        <nav style={{ display: "flex", gap: "16px", marginTop: "12px", flexWrap: "wrap" }}>
          <Link to="/">Home</Link>
          <Link to="/claims">Claims</Link>
          <Link to="/statements">Statements</Link>
          {claimId && <Link to={`/claims/${claimId}`}>Claim</Link>}
          {statementId && <Link to={`/statements/${statementId}`}>Statement</Link>}
        </nav>
      </header>

      <div style={cardStyle}>
        <Row label="Claim Id" value={claimId || "N/A"} />
        <Row label="Statement Id" value={statementId || "N/A"} />
        <Row label="Workspace Status" value="Ready for contradiction implementation" />
      </div>

      {claimId && claimQuery.isSuccess && claimQuery.data && (
        <div style={cardStyle}>
          <h3 style={{ marginTop: 0 }}>Claim Context</h3>
          <Row label="Topic" value={claimQuery.data.topic} />
          <Row label="Type" value={claimQuery.data.type} />
          <Row label="Status" value={claimQuery.data.status} />
          <Row label="Normalized Text" value={claimQuery.data.normalizedText} />
        </div>
      )}

      {statementId && statementQuery.isSuccess && statementQuery.data && (
        <div style={cardStyle}>
          <h3 style={{ marginTop: 0 }}>Statement Context</h3>
          <Row label="Text" value={statementQuery.data.text} />
          <Row label="Topic" value={statementQuery.data.topic ?? "N/A"} />
          <Row label="Polarity" value={statementQuery.data.polarity} />
          <Row label="Status" value={statementQuery.data.status} />
        </div>
      )}

      <div style={cardStyle}>
        <h3 style={{ marginTop: 0 }}>Planned contradiction actions</h3>
        <ul style={{ marginBottom: 0 }}>
          <li>Compare one claim against alternate claims and statements</li>
          <li>Classify contradiction type and severity</li>
          <li>Link contradiction results to case review workflow</li>
        </ul>
      </div>

      <div style={{ display: "flex", gap: "12px", flexWrap: "wrap" }}>
        {claimId && <Link to={`/claims/${claimId}`} style={actionLinkStyle}>Back to Claim</Link>}
        {statementId && <Link to={`/claims/workspace?statementId=${statementId}`} style={actionLinkStyle}>Open Claims Workspace</Link>}
        <Link to="/claims" style={actionLinkStyle}>Open Claims</Link>
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

const actionLinkStyle: React.CSSProperties = {
  padding: "10px 16px",
  borderRadius: "8px",
  border: "1px solid #1976d2",
  textDecoration: "none",
  color: "inherit",
};
