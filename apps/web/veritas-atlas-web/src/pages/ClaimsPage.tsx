import { Link, useSearchParams } from "react-router-dom";
import { useMemo } from "react";
import { useClaims } from "../hooks/useClaims";

export function ClaimsPage() {
  const [params] = useSearchParams();
  const statementId = params.get("statementId") ?? undefined;
  const topicFilter = params.get("topic") ?? "";
  const typeFilter = params.get("type") ?? "All";
  const materialFilter = params.get("material") ?? "All";

  const query = useClaims(statementId);
  const items = query.data?.items ?? [];

  const filteredItems = useMemo(() => {
    return items.filter((item) => {
      const matchesTopic =
        topicFilter.length === 0 ||
        item.topic.toLowerCase().includes(topicFilter.toLowerCase()) ||
        item.normalizedText.toLowerCase().includes(topicFilter.toLowerCase());

      const matchesType =
        typeFilter === "All" || item.type === typeFilter;

      const matchesMaterial =
        materialFilter === "All" ||
        (materialFilter === "Material" && item.isMaterial) ||
        (materialFilter === "NonMaterial" && !item.isMaterial);

      return matchesTopic && matchesType && matchesMaterial;
    });
  }, [items, topicFilter, typeFilter, materialFilter]);

  const types = useMemo(
    () => ["All", ...Array.from(new Set(items.map((x) => x.type))).sort()],
    [items]
  );

  return (
    <div style={{ fontFamily: "Arial, sans-serif", padding: "24px" }}>
      <header style={{ marginBottom: "24px" }}>
        <h1 style={{ margin: 0 }}>Claims</h1>
        <p style={{ color: "#555" }}>Live claim catalog from Veritas Atlas API</p>
        <nav style={{ display: "flex", gap: "16px", marginTop: "12px", flexWrap: "wrap" }}>
          <Link to="/">Home</Link>
          <Link to="/statements">Statements</Link>
          <Link to="/claims">Claims</Link>
          <Link to="/claims/workspace">Claims Workspace</Link>
          <Link to="/contradictions/workspace">Contradictions Workspace</Link>
        </nav>
      </header>

      <div style={{ marginBottom: "16px", display: "flex", gap: "12px", flexWrap: "wrap" }}>
        <Link to={statementId ? `/claims/workspace?statementId=${statementId}` : "/claims/workspace"} style={actionLinkStyle}>Open Claims Workspace</Link>
        <Link to="/contradictions/workspace" style={actionLinkStyle}>Open Contradictions Workspace</Link>
      </div>

      {statementId && (
        <div style={hintCardStyle}>
          <strong>Statement scope:</strong> {statementId}
        </div>
      )}

      <section style={filterPanelStyle}>
        <input value={topicFilter} readOnly style={inputStyle} placeholder="Use URL ?topic=... for deep link filtering" />
        <select value={typeFilter} disabled style={selectStyle}>
          {types.map((value) => (
            <option key={value} value={value}>{value}</option>
          ))}
        </select>
        <select value={materialFilter} disabled style={selectStyle}>
          <option value="All">All</option>
          <option value="Material">Material</option>
          <option value="NonMaterial">Non-material</option>
        </select>
      </section>

      {query.isLoading && <p>Loading claims...</p>}

      {query.isError && (
        <p style={{ color: "crimson" }}>
          Failed to load claims: {(query.error as Error).message}
        </p>
      )}

      {query.isSuccess && (
        <>
          <p>Showing {filteredItems.length} of {query.data.totalCount} claims</p>

          {filteredItems.length === 0 ? (
            <div style={emptyStateStyle}>
              <p style={{ margin: 0 }}>No claims found.</p>
            </div>
          ) : (
            <div style={{ overflowX: "auto" }}>
              <table style={tableStyle}>
                <thead>
                  <tr>
                    <th style={thStyle}>Topic</th>
                    <th style={thStyle}>Type</th>
                    <th style={thStyle}>Status</th>
                    <th style={thStyle}>Material</th>
                    <th style={thStyle}>Statement</th>
                    <th style={thStyle}>Actions</th>
                  </tr>
                </thead>
                <tbody>
                  {filteredItems.map((item) => (
                    <tr key={item.id}>
                      <td style={tdStyle}><Link to={`/claims/${item.id}`}>{item.topic}</Link></td>
                      <td style={tdStyle}>{item.type}</td>
                      <td style={tdStyle}>{item.status}</td>
                      <td style={tdStyle}>{item.isMaterial ? "Yes" : "No"}</td>
                      <td style={tdStyle}><Link to={`/statements/${item.statementId}`}>{item.statementId}</Link></td>
                      <td style={tdStyle}>
                        <div style={{ display: "flex", gap: "8px", flexWrap: "wrap" }}>
                          <Link to={`/claims/${item.id}`}>Detail</Link>
                          <Link to={`/claims/workspace?statementId=${item.statementId}`}>Workspace</Link>
                          <Link to={`/contradictions/workspace?claimId=${item.id}&statementId=${item.statementId}`}>Contradictions</Link>
                        </div>
                      </td>
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

const filterPanelStyle: React.CSSProperties = {
  display: "grid",
  gridTemplateColumns: "minmax(260px, 1fr) 180px 180px",
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
  background: "#fafafa",
};

const selectStyle: React.CSSProperties = {
  width: "100%",
  boxSizing: "border-box",
  padding: "10px 12px",
  borderRadius: "8px",
  border: "1px solid #ccc",
  font: "inherit",
  background: "#fafafa",
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
