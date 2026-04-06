import { Link, useParams } from "react-router-dom";
import { useClaimDetail } from "../hooks/useClaimDetail";

export function ClaimDetailPage() {
  const { id } = useParams();
  const query = useClaimDetail(id);

  if (query.isLoading) {
    return <div style={{ fontFamily: "Arial, sans-serif", padding: "24px" }}>Loading claim...</div>;
  }

  if (query.isError) {
    return <div style={{ fontFamily: "Arial, sans-serif", padding: "24px", color: "crimson" }}>Failed to load claim: {(query.error as Error).message}</div>;
  }

  if (!query.data) {
    return <div style={{ fontFamily: "Arial, sans-serif", padding: "24px" }}>Claim not found.</div>;
  }

  const item = query.data;

  return (
    <div style={{ fontFamily: "Arial, sans-serif", padding: "24px" }}>
      <nav style={{ display: "flex", gap: "16px", marginBottom: "20px", flexWrap: "wrap" }}>
        <Link to="/claims">Back to Claims</Link>
        <Link to={`/statements/${item.statementId}`}>Statement</Link>
        {item.caseId && <Link to={`/cases/${item.caseId}`}>Case</Link>}
        <Link to={`/contradictions/workspace?claimId=${item.id}&statementId=${item.statementId}`}>Contradictions Workspace</Link>
      </nav>

      <h1 style={{ marginTop: 0 }}>Claim</h1>

      <div style={cardStyle}>
        <Row label="Id" value={item.id} />
        <Row label="Statement Id" value={item.statementId} />
        <Row label="Topic" value={item.topic} />
        <Row label="Normalized Text" value={item.normalizedText} />
        <Row label="Type" value={item.type} />
        <Row label="Status" value={item.status} />
        <Row label="Material" value={item.isMaterial ? "Yes" : "No"} />
        <Row label="Person Id" value={item.personId ?? "N/A"} />
        <Row label="Case Id" value={item.caseId ?? "N/A"} />
        <Row label="Created" value={new Date(item.createdAtUtc).toLocaleString()} />
      </div>

      <div style={{ display: "flex", gap: "12px", flexWrap: "wrap" }}>
        <Link to={`/statements/${item.statementId}`} style={actionLinkStyle}>Open Statement</Link>
        <Link to={`/claims?statementId=${item.statementId}`} style={actionLinkStyle}>More Claims for Statement</Link>
        <Link to={`/contradictions/workspace?claimId=${item.id}&statementId=${item.statementId}`} style={actionLinkStyle}>Open Contradictions Workspace</Link>
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
