import { Link, useParams } from "react-router-dom";
import { useStatementDetail } from "../hooks/useStatementDetail";

export function StatementDetailPage() {
  const { id } = useParams();
  const query = useStatementDetail(id);

  if (query.isLoading) {
    return <div style={{ fontFamily: "Arial, sans-serif", padding: "24px" }}>Loading statement...</div>;
  }

  if (query.isError) {
    return <div style={{ fontFamily: "Arial, sans-serif", padding: "24px", color: "crimson" }}>Failed to load statement: {(query.error as Error).message}</div>;
  }

  if (!query.data) {
    return <div style={{ fontFamily: "Arial, sans-serif", padding: "24px" }}>Statement not found.</div>;
  }

  const item = query.data;

  return (
    <div style={{ fontFamily: "Arial, sans-serif", padding: "24px" }}>
      <nav style={{ display: "flex", gap: "16px", marginBottom: "20px", flexWrap: "wrap" }}>
        <Link to="/statements">Back to Statements</Link>
        {item.evidenceId && <Link to={`/evidence/${item.evidenceId}`}>Evidence</Link>}
      </nav>

      <h1 style={{ marginTop: 0 }}>Statement</h1>

      <div style={cardStyle}>
        <Row label="Id" value={item.id} />
        <Row label="Text" value={item.text} />
        <Row label="Topic" value={item.topic ?? "N/A"} />
        <Row label="Predicate" value={item.predicate ?? "N/A"} />
        <Row label="Object" value={item.object ?? "N/A"} />
        <Row label="Polarity" value={item.polarity} />
        <Row label="Status" value={item.status} />
        <Row label="Evidence Id" value={item.evidenceId ?? "N/A"} />
        <Row label="Person Id" value={item.personId ?? "N/A"} />
        <Row label="Created" value={new Date(item.createdAt).toLocaleString()} />
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
