import { Link } from "react-router-dom";
import { useMemo, useState, type CSSProperties } from "react";
import { useSources } from "../hooks/useSources";
import type { SourceItem, SourceReferenceResponse } from "../api/sources";

export function SourcesPage() {
  const [search, setSearch] = useState("");
  const [typeFilter, setTypeFilter] = useState("All");
  const [statusFilter, setStatusFilter] = useState("All");

  const sourcesQuery = useSources({
    search: search || undefined,
    type: typeFilter,
    status: statusFilter,
  });

  const items = sourcesQuery.data?.items ?? [];

  const types = useMemo(
    () => ["All", ...Array.from(new Set(items.map((x) => x.type))).sort()],
    [items]
  );

  const statuses = useMemo(
    () => ["All", ...Array.from(new Set(items.map((x) => x.status))).sort()],
    [items]
  );

  return (
    <div style={{ fontFamily: "Arial, sans-serif", padding: "24px" }}>
      <header style={{ marginBottom: "24px" }}>
        <h1 style={{ margin: 0 }}>Sources</h1>
        <p style={{ color: "#555" }}>Live source catalog from Veritas Atlas API</p>
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
        <Link to="/sources/new" style={actionLinkStyle}>Create New Source</Link>
        <Link to="/ingestion" style={actionLinkStyle}>Open Ingestion Workspace</Link>
      </div>

      <section style={filterPanelStyle}>
        <input
          value={search}
          onChange={(e) => setSearch(e.target.value)}
          placeholder="Search name, type, status, domain, url, or external id"
          style={inputStyle}
        />
        <select value={typeFilter} onChange={(e) => setTypeFilter(e.target.value)} style={selectStyle}>
          {types.map((value) => (
            <option key={value} value={value}>{value}</option>
          ))}
        </select>
        <select value={statusFilter} onChange={(e) => setStatusFilter(e.target.value)} style={selectStyle}>
          {statuses.map((value) => (
            <option key={value} value={value}>{value}</option>
          ))}
        </select>
      </section>

      {sourcesQuery.isLoading && <p>Loading sources...</p>}

      {sourcesQuery.isError && (
        <p style={{ color: "crimson" }}>
          Failed to load sources: {(sourcesQuery.error as Error).message}
        </p>
      )}

      {sourcesQuery.isSuccess && (
        <>
          <p>
            Showing {items.length} of {sourcesQuery.data.totalCount} sources
          </p>

          {items.length === 0 ? (
            <div style={emptyStateStyle}>
              <p style={{ margin: 0 }}>No sources match the current filters.</p>
            </div>
          ) : (
            <div style={{ overflowX: "auto" }}>
              <table style={tableStyle}>
                <thead>
                  <tr>
                    <th style={thStyle}>Name</th>
                    <th style={thStyle}>Type</th>
                    <th style={thStyle}>Reference</th>
                    <th style={thStyle}>Status</th>
                    <th style={thStyle}>Created</th>
                  </tr>
                </thead>
                <tbody>
                  {items.map((item: SourceItem) => (
                    <tr key={item.id}>
                      <td style={tdStyle}>{item.name}</td>
                      <td style={tdStyle}>{item.type}</td>
                      <td style={tdStyle}>{renderReference(item.reference)}</td>
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

function renderReference(reference: SourceReferenceResponse | null) {
  if (!reference) {
    return "N/A";
  }

  return (
    <div style={{ display: "grid", gap: "4px" }}>
      {reference.url && (
        <a href={reference.url} target="_blank" rel="noreferrer">
          {reference.url}
        </a>
      )}
      {reference.domain && <span>Domain: {reference.domain}</span>}
      {reference.externalId && <span>ExternalId: {reference.externalId}</span>}
      {reference.languageCode && <span>Lang: {reference.languageCode}</span>}
    </div>
  );
}

function formatDate(value: string) {
  return new Date(value).toLocaleString();
}

const filterPanelStyle: CSSProperties = {
  display: "grid",
  gridTemplateColumns: "minmax(260px, 1fr) 180px 180px",
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
