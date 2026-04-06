import { Link, useParams } from "react-router-dom";
import { useContradictionDetail } from "../hooks/useContradictionDetail";
import { ContradictionSignalPanel } from "../components/ContradictionSignalPanel";

export function ContradictionDetailPage() {
  const { id } = useParams();
  const query = useContradictionDetail(id);

  if (query.isLoading) {
    return <div style={{ fontFamily: "Arial, sans-serif", padding: "24px" }}>Loading contradiction...</div>;
  }

  if (query.isError) {
    return <div style={{ fontFamily: "Arial, sans-serif", padding: "24px", color: "crimson" }}>Failed to load contradiction: {(query.error as Error).message}</div>;
  }

  if (!query.data) {
    return <div style={{ fontFamily: "Arial, sans-serif", padding: "24px" }}>Contradiction not found.</div>;
  }

  const item = query.data;

  return (
    <div style={{ fontFamily: "Arial, sans-serif", padding: "24px" }}>
      <nav style={{ display: "flex", gap: "16px", marginBottom: "20px", flexWrap: "wrap" }}>
        <Link to="/contradictions">Back to Contradictions</Link>
        <Link to={`/claims/${item.primaryClaimId}`}>Primary Claim</Link>
        <Link to={`/claims/${item.secondaryClaimId}`}>Secondary Claim</Link>
      </nav>

      <h1 style={{ marginTop: 0 }}>Contradiction</h1>

      <div style={cardStyle}>
        <Row label="Id" value={item.id} />
        <Row label="Topic" value={item.topic} />
        <Row label="Summary" value={item.summary} />
        <Row label="Type" value={item.contradictionType} />
        <Row label="Severity" value={item.severity} />
        <Row label="Status" value={item.status} />
        <Row label="Primary Claim" value={item.primaryClaimId} />
        <Row label="Secondary Claim" value={item.secondaryClaimId} />
        <Row label="Case Id" value={item.caseId ?? "N/A"} />
      </div>

      <ContradictionSignalPanel />
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