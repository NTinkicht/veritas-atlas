import { Link, useSearchParams } from "react-router-dom";
import { useMemo } from "react";
import { useStatements } from "../hooks/useStatements";

export function StatementsPage() {
  const [params, setParams] = useSearchParams();

  const forcedEvidenceId = params.get("evidenceId") ?? "";
  const search = params.get("search") ?? "";
  const statusFilter = params.get("status") ?? "All";
  const polarityFilter = params.get("polarity") ?? "All";
  const topicFilter = params.get("topic") ?? "All";

  const query = useStatements();
  const items = query.data?.items ?? [];

  const filteredItems = useMemo(() => {
    const term = search.trim().toLowerCase();

    return items.filter((item) => {
      const matchesEvidence =
        forcedEvidenceId.length === 0 || item.evidenceId === forcedEvidenceId;

      const matchesSearch =
        term.length === 0 ||
        item.text.toLowerCase().includes(term) ||
        (item.topic ?? "").toLowerCase().includes(term) ||
        (item.predicate ?? "").toLowerCase().includes(term) ||
        (item.object ?? "").toLowerCase().includes(term);

      const matchesStatus =
        statusFilter === "All" || item.status === statusFilter;

      const matchesPolarity =
        polarityFilter === "All" || item.polarity === polarityFilter;

      const matchesTopic =
        topicFilter === "All" || (item.topic ?? "N/A") === topicFilter;

      return matchesEvidence && matchesSearch && matchesStatus && matchesPolarity && matchesTopic;
    });
  }, [items, forcedEvidenceId, search, statusFilter, polarityFilter, topicFilter]);

  const statuses = useMemo(
    () => ["All", ...Array.from(new Set(items.map((x) => x.status))).sort()],
    [items]
  );

  const polarities = useMemo(
    () => ["All", ...Array.from(new Set(items.map((x) => x.polarity))).sort()],
    [items]
  );

  const topics = useMemo(
    () => ["All", ...Array.from(new Set(items.map((x) => x.topic ?? "N/A"))).sort()],
    [items]
  );

  const updateParam = (key: string, value: string) => {
    const next = new URLSearchParams(params);
    if (!value || value === "All") {
      next.delete(key);
    } else {
      next.set(key, value);
    }
    setParams(next);
  };

  return (
    <div style={{ fontFamily: "Arial, sans-serif", padding: "24px" }}>
      <header style={{ marginBottom: "24px" }}>
        <h1 style={{ margin: 0 }}>Statements</h1>
        <p style={{ color: "#555" }}>Live statement catalog from Veritas Atlas API</p>
        <nav style={{ display: "flex", gap: "16px", marginTop: "12px", flexWrap: "wrap" }}>
          <Link to="/">Home</Link>
          <Link to="/dashboard">Dashboard</Link>
          <Link to="/sources">Sources</Link>
          <Link to="/documents">Documents</Link>
          <Link to="/evidence">Evidence</Link>
          <Link to="/statements">Statements</Link>
        </nav>
      </header>

      <div style={{ marginBottom: "16px", display: "flex", gap: "12px", flexWrap: "wrap" }}>
        <Link to="/statements/new" style={actionLinkStyle}>Create New Statement</Link>
      </div>

      <section style={filterPanelStyle}>
        <input
          value={search}
          onChange={(e) => updateParam("search", e.target.value)}
          placeholder="Search statement text, topic, predicate, or object"
          style={inputStyle}
        />
        <select value={statusFilter} onChange={(e) => updateParam("status", e.target.value)} style={selectStyle}>
          {statuses.map((value) => (
            <option key={value} value={value}>{value}</option>
          ))}
        </select>
        <select value={polarityFilter} onChange={(e) => updateParam("polarity", e.target.value)} style={selectStyle}>
          {polarities.map((value) => (
            <option key={value} value={value}>{value}</option>
          ))}
        </select>
        <select value={topicFilter} onChange={(e) => updateParam("topic", e.target.value)} style={selectStyle}>
          {topics.map((value) => (
            <option key={value} value={value}>{value}</option>
          ))}
        </select>
      </section>

      {forcedEvidenceId && (
        <div style={hintCardStyle}>
          <strong>Evidence scope:</strong> {forcedEvidenceId}
        </div>
      )}

      {query.isLoading && <p>Loading statements...</p>}

      {query.isError && (
        <p style={{ color: "crimson" }}>
          Failed to load statements: {(query.error as Error).message}
        </p>
      )}

      {query.isSuccess && (
        <>
          <p>Showing {filteredItems.length} of {query.data.total} statements</p>

          {filteredItems.length === 0 ? (
            <div style={emptyStateStyle}>
              <p style={{ margin: 0 }}>No statements found for the current filters.</p>
            </div>
          ) : (
            <div style={{ overflowX: "auto" }}>
              <table style={tableStyle}>
                <thead>
                  <tr>
                    <th style={thStyle}>Text</th>
                    <th style={thStyle}>Topic</th>
                    <th style={thStyle}>Polarity</th>
                    <th style={thStyle}>Status</th>
                    <th style={thStyle}>Evidence</th>
                    <th style={thStyle}>Created</th>
                  </tr>
                </thead>
                <tbody>
                  {filteredItems.map((item) => (
                    <tr key={item.id}>
                      <td style={tdStyle}><Link to={`/statements/${item.id}`}>{item.text}</Link></td>
                      <td style={tdStyle}>{item.topic ?? "N/A"}</td>
                      <td style={tdStyle}>{item.polarity}</td>
                      <td style={tdStyle}>{item.status}</td>
                      <td style={tdStyle}>{item.evidenceId ? <Link to={`/evidence/${item.evidenceId}`}>{item.evidenceId}</Link> : "N/A"}</td>
                      <td style={tdStyle}>{new Date(item.createdAt).toLocaleString()}</td>
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

const filterPanelStyle: React.CSSProperties = {
  display: "grid",
  gridTemplateColumns: "minmax(260px, 1fr) 180px 180px 180px",
  gap: "12px",
  marginBottom: "16px",
};

const inputStyle: React.CSSProperties = {
  width: "100%",
  boxSizing: "border-box",
  padding: "10px 12px",
  borderRadius: "8px",
  border: "1px solid #ccc",
  font: "inherit",
};

const selectStyle: React.CSSProperties = {
  width: "100%",
  boxSizing: "border-box",
  padding: "10px 12px",
  borderRadius: "8px",
  border: "1px solid #ccc",
  font: "inherit",
};

const tableStyle: React.CSSProperties = {
  width: "100%",
  borderCollapse: "collapse",
  marginTop: "16px",
};

const thStyle: React.CSSProperties = {
  textAlign: "left",
  borderBottom: "1px solid #ccc",
  padding: "10px",
};

const tdStyle: React.CSSProperties = {
  borderBottom: "1px solid #eee",
  padding: "10px",
  verticalAlign: "top",
};

const actionLinkStyle: React.CSSProperties = {
  padding: "10px 16px",
  borderRadius: "8px",
  border: "1px solid #1976d2",
  textDecoration: "none",
  color: "inherit",
  display: "inline-flex",
  alignItems: "center",
};

const emptyStateStyle: React.CSSProperties = {
  border: "1px solid #eee",
  borderRadius: "12px",
  padding: "20px",
  color: "#666",
};

const hintCardStyle: React.CSSProperties = {
  marginBottom: "16px",
  padding: "12px 14px",
  border: "1px solid #d9e6ff",
  borderRadius: "12px",
  background: "#f8fbff",
};
