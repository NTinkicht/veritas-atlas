import { Link } from "react-router-dom";
import { useMemo, useState, type CSSProperties } from "react";
import { useEvidenceList } from "../hooks/useEvidenceList";
import type { EvidenceItem } from "../api/evidenceList";

export function EvidencePage() {
  const [documentIdFilter, setDocumentIdFilter] = useState("");
  const [statusFilter, setStatusFilter] = useState("All");

  const evidenceQuery = useEvidenceList({
    documentId: documentIdFilter || undefined,
    status: statusFilter,
  });

  const items = evidenceQuery.data?.items ?? [];

  const statuses = useMemo(
    () => ["All", ...Array.from(new Set(items.map((x) => x.status))).sort()],
    [items]
  );

  return (
    <div style={{ fontFamily: "Arial, sans-serif", padding: "24px" }}>
      <header style={{ marginBottom: "24px" }}>
        <h1 style={{ margin: 0 }}>Evidence</h1>
        <p style={{ color: "#555" }}>Live evidence catalog from Veritas Atlas API</p>
        <nav style={{ display: "flex", gap: "16px", marginTop: "12px", flexWrap: "wrap" }}>
          <Link to="/">Home</Link>
          <Link to="/dashboard">Dashboard</Link>
          <Link to="/ingestion">Ingestion</Link>
          <Link to="/sources">Sources</Link>
          <Link to="/documents">Documents</Link>
          <Link to="/evidence">Evidence</Link>
          <Link to="/statements/new">New Statement</Link>
        </nav>
      </header>

      <div style={{ marginBottom: "16px", display: "flex", gap: "12px", flexWrap: "wrap" }}>
        <Link to="/evidence/new" style={actionLinkStyle}>Create New Evidence</Link>
        <Link to="/ingestion" style={actionLinkStyle}>Open Ingestion Workspace</Link>
      </div>

      <section style={filterPanelStyle}>
        <input
          value={documentIdFilter}
          onChange={(e) => setDocumentIdFilter(e.target.value)}
          placeholder="Filter by document id"
          style={inputStyle}
        />
        <select value={statusFilter} onChange={(e) => setStatusFilter(e.target.value)} style={selectStyle}>
          {statuses.map((value) => (
            <option key={value} value={value}>{value}</option>
          ))}
        </select>
      </section>

      {evidenceQuery.isLoading && <p>Loading evidence...</p>}

      {evidenceQuery.isError && (
        <p style={{ color: "crimson" }}>
          Failed to load evidence: {(evidenceQuery.error as Error).message}
        </p>
      )}

      {evidenceQuery.isSuccess && (
        <>
          <p>
            Showing {items.length} of {evidenceQuery.data.totalCount} evidence items
          </p>

          {items.length === 0 ? (
            <div style={emptyStateStyle}>
              <p style={{ margin: 0 }}>No evidence items match the current filters.</p>
            </div>
          ) : (
            <div style={{ overflowX: "auto" }}>
              <table style={tableStyle}>
                <thead>
                  <tr>
                    <th style={thStyle}>Id</th>
                    <th style={thStyle}>Document Id</th>
                    <th style={thStyle}>Status</th>
                    <th style={thStyle}>Created</th>
                  </tr>
                </thead>
                <tbody>
                  {items.map((item: EvidenceItem) => (
                    <tr key={item.id}>
                      <td style={tdStyle}>{item.id}</td>
                      <td style={tdStyle}>{item.documentId ?? "N/A"}</td>
                      <td style={tdStyle}>{item.status}</td>
                      <td style={tdStyle}>{formatDate(item.createdAtUtc)}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </>
      )}
    </div>
  );
}

function formatDate(value: string) {
  return new Date(value).toLocaleString();
}

const filterPanelStyle: CSSProperties = {
  display: "grid",
  gridTemplateColumns: "minmax(260px, 1fr) 180px",
  gap: "12px",
  marginBottom: "16px",
};

const tableStyle: CSSProperties = {
  width: "100%",
  borderCollapse: "collapse",
  marginTop: "16px",
};

const thStyle: CSSProperties = {
  textAlign: "left",
  borderBottom: "1px solid #ccc",
  padding: "10px",
};

const tdStyle: CSSProperties = {
  borderBottom: "1px solid #eee",
  padding: "10px",
  verticalAlign: "top",
};

const actionLinkStyle: CSSProperties = {
  padding: "10px 16px",
  borderRadius: "8px",
  border: "1px solid #1976d2",
  textDecoration: "none",
  color: "inherit",
  display: "inline-flex",
  alignItems: "center",
};

const inputStyle: CSSProperties = {
  width: "100%",
  boxSizing: "border-box",
  padding: "10px 12px",
  borderRadius: "8px",
  border: "1px solid #ccc",
  font: "inherit",
};

const selectStyle: CSSProperties = {
  width: "100%",
  boxSizing: "border-box",
  padding: "10px 12px",
  borderRadius: "8px",
  border: "1px solid #ccc",
  font: "inherit",
};

const emptyStateStyle: CSSProperties = {
  border: "1px solid #eee",
  borderRadius: "12px",
  padding: "20px",
  color: "#666",
};
