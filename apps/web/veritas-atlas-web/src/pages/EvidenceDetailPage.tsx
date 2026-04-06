import { Link, useParams } from "react-router-dom";
import { useEvidenceDetail } from "../hooks/useEvidenceDetail";
import { useStatements } from "../hooks/useStatements";

export function EvidenceDetailPage() {
  const { id } = useParams();
  const query = useEvidenceDetail(id);
  const statementsQuery = useStatements();

  if (query.isLoading) {
    return <div style={{ fontFamily: "Arial, sans-serif", padding: "24px" }}>Loading evidence...</div>;
  }

  if (query.isError) {
    return <div style={{ fontFamily: "Arial, sans-serif", padding: "24px", color: "crimson" }}>Failed to load evidence: {(query.error as Error).message}</div>;
  }

  if (!query.data) {
    return <div style={{ fontFamily: "Arial, sans-serif", padding: "24px" }}>Evidence not found.</div>;
  }

  const item = query.data;
  const relatedStatements = (statementsQuery.data?.items ?? []).filter((x) => x.evidenceId === item.id);

  return (
    <div style={{ fontFamily: "Arial, sans-serif", padding: "24px" }}>
      <nav style={{ display: "flex", gap: "16px", marginBottom: "20px", flexWrap: "wrap" }}>
        <Link to="/evidence">Back to Evidence</Link>
        {item.documentId && <Link to={`/documents/${item.documentId}`}>Document</Link>}
        <Link to="/statements">Statements</Link>
      </nav>

      <h1 style={{ marginTop: 0 }}>Evidence {item.id}</h1>

      <div style={cardStyle}>
        <Row label="Id" value={item.id} />
        <Row label="Source Id" value={item.sourceId} />
        <Row label="Document Id" value={item.documentId ?? "N/A"} />
        <Row label="Type" value={item.type} />
        <Row label="Status" value={item.status} />
        <Row label="LanguageCode" value={item.languageCode ?? "N/A"} />
        <Row label="ContentHash" value={item.contentHash ?? "N/A"} />
        <Row label="Captured" value={item.capturedAtUtc ? new Date(item.capturedAtUtc).toLocaleString() : "N/A"} />
        <Row label="Span" value={item.span ? `${item.span.startOffset} - ${item.span.endOffset}` : "N/A"} />
        <Row label="Created" value={new Date(item.createdAtUtc).toLocaleString()} />
        <Row label="Updated" value={new Date(item.updatedAtUtc).toLocaleString()} />
      </div>

      <div style={cardStyle}>
        <h3 style={{ marginTop: 0 }}>Content</h3>
        <pre style={{ whiteSpace: "pre-wrap", margin: 0, fontFamily: "inherit" }}>{item.content}</pre>
      </div>

      <div style={cardStyle}>
        <h3 style={{ marginTop: 0 }}>Statements</h3>
        <div style={{ display: "flex", gap: "12px", flexWrap: "wrap", marginBottom: "12px" }}>
          <Link to={`/statements/new?evidenceId=${item.id}`} style={actionLinkStyle}>Create Statement for this Evidence</Link>
          <Link to="/statements" style={actionLinkStyle}>Open Statements</Link>
        </div>

        {statementsQuery.isLoading && <p>Loading statements...</p>}
        {statementsQuery.isSuccess && relatedStatements.length === 0 && <p>No statements are linked to this evidence yet.</p>}
        {statementsQuery.isSuccess && relatedStatements.length > 0 && (
          <ul>
            {relatedStatements.map((statement) => (
              <li key={statement.id}>
                <Link to={`/statements/${statement.id}`}>{statement.text}</Link>
              </li>
            ))}
          </ul>
        )}
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
