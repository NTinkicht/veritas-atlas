import { Link, useParams } from "react-router-dom";
import { useSourceDetail } from "../hooks/useSourceDetail";

export function SourceDetailPage() {
  const { id } = useParams();
  const query = useSourceDetail(id);

  if (query.isLoading) {
    return <div style={{ fontFamily: "Arial, sans-serif", padding: "24px" }}>Loading source...</div>;
  }

  if (query.isError) {
    return <div style={{ fontFamily: "Arial, sans-serif", padding: "24px", color: "crimson" }}>Failed to load source: {(query.error as Error).message}</div>;
  }

  if (!query.data) {
    return <div style={{ fontFamily: "Arial, sans-serif", padding: "24px" }}>Source not found.</div>;
  }

  const item = query.data;

  return (
    <div style={{ fontFamily: "Arial, sans-serif", padding: "24px" }}>
      <nav style={{ display: "flex", gap: "16px", marginBottom: "20px", flexWrap: "wrap" }}>
        <Link to="/sources">Back to Sources</Link>
        <Link to="/documents?sourceIdPlaceholder=1">Documents</Link>
        <Link to="/documents/new">New Document</Link>
      </nav>

      <h1 style={{ marginTop: 0 }}>{item.name}</h1>

      <div style={cardStyle}>
        <Row label="Id" value={item.id} />
        <Row label="Type" value={item.type} />
        <Row label="Status" value={item.status} />
        <Row label="Trust Tier" value={item.trustTier} />
        <Row label="Description" value={item.description ?? "N/A"} />
        <Row label="Created" value={formatDate(item.createdAtUtc)} />
        <Row label="Updated" value={formatDate(item.updatedAtUtc)} />
      </div>

      <div style={cardStyle}>
        <h3 style={{ marginTop: 0 }}>Reference</h3>
        <Row label="ExternalId" value={item.reference?.externalId ?? "N/A"} />
        <Row label="Url" value={item.reference?.url ?? "N/A"} />
        <Row label="Domain" value={item.reference?.domain ?? "N/A"} />
        <Row label="LanguageCode" value={item.reference?.languageCode ?? "N/A"} />
      </div>

      <div style={{ display: "flex", gap: "12px", flexWrap: "wrap" }}>
        <Link to={`/documents?sourceId=${item.id}`} style={actionLinkStyle}>View Documents For This Source</Link>
        <Link to="/documents/new" style={actionLinkStyle}>Create Document</Link>
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
