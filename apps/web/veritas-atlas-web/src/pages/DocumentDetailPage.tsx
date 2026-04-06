import { Link, useParams } from "react-router-dom";
import { useDocumentDetail } from "../hooks/useDocumentDetail";

export function DocumentDetailPage() {
  const { id } = useParams();
  const query = useDocumentDetail(id);

  if (query.isLoading) {
    return <div style={{ fontFamily: "Arial, sans-serif", padding: "24px" }}>Loading document...</div>;
  }

  if (query.isError) {
    return <div style={{ fontFamily: "Arial, sans-serif", padding: "24px", color: "crimson" }}>Failed to load document: {(query.error as Error).message}</div>;
  }

  if (!query.data) {
    return <div style={{ fontFamily: "Arial, sans-serif", padding: "24px" }}>Document not found.</div>;
  }

  const item = query.data;

  return (
    <div style={{ fontFamily: "Arial, sans-serif", padding: "24px" }}>
      <nav style={{ display: "flex", gap: "16px", marginBottom: "20px", flexWrap: "wrap" }}>
        <Link to="/documents">Back to Documents</Link>
        <Link to={`/sources/${item.sourceId}`}>Source</Link>
        <Link to="/evidence/new">New Evidence</Link>
      </nav>

      <h1 style={{ marginTop: 0 }}>{item.title}</h1>

      <div style={cardStyle}>
        <Row label="Id" value={item.id} />
        <Row label="Source Id" value={item.sourceId} />
        <Row label="Type" value={item.type} />
        <Row label="Status" value={item.status} />
        <Row label="LanguageCode" value={item.languageCode ?? "N/A"} />
        <Row label="ExternalId" value={item.externalId ?? "N/A"} />
        <Row label="Url" value={item.url ?? "N/A"} />
        <Row label="ContentHash" value={item.contentHash ?? "N/A"} />
        <Row label="Published" value={item.publishedAtUtc ? formatDate(item.publishedAtUtc) : "N/A"} />
        <Row label="Retrieved" value={item.retrievedAtUtc ? formatDate(item.retrievedAtUtc) : "N/A"} />
        <Row label="Created" value={formatDate(item.createdAtUtc)} />
        <Row label="Updated" value={formatDate(item.updatedAtUtc)} />
      </div>

      <div style={{ display: "flex", gap: "12px", flexWrap: "wrap" }}>
        <Link to={`/sources/${item.sourceId}`} style={actionLinkStyle}>Open Source</Link>
        <Link to={`/evidence?documentId=${item.id}`} style={actionLinkStyle}>View Evidence For This Document</Link>
        <Link to="/evidence/new" style={actionLinkStyle}>Create Evidence</Link>
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

function formatDate(value: string) {
  return new Date(value).toLocaleString();
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
