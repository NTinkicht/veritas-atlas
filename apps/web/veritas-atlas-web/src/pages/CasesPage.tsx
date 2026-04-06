import { Link } from "react-router-dom";
import type { CSSProperties } from "react";
import { useCases } from "../hooks/useCases";
import type { CasesListItem } from "../api/cases";

export function CasesPage() {
  const casesQuery = useCases();

  return (
    <div style={{ fontFamily: "Arial, sans-serif", padding: "24px" }}>
      <header style={{ marginBottom: "24px" }}>
        <h1 style={{ margin: 0 }}>Cases</h1>
        <p style={{ color: "#555" }}>Live data from Veritas Atlas API</p>
        <nav style={{ display: "flex", gap: "16px", marginTop: "12px", flexWrap: "wrap" }}>
          <Link to="/">Home</Link>
          <Link to="/dashboard">Dashboard</Link>
          <Link to="/health">Health</Link>
          <Link to="/cases">Cases</Link>
          <Link to="/cases/new">New Case</Link>
          <Link to="/reviews">Reviews</Link>
        </nav>
      </header>

      <div style={{ marginBottom: "16px" }}>
        <Link to="/cases/new" style={newCaseLinkStyle}>Create New Case</Link>
      </div>

      {casesQuery.isLoading && <p>Loading cases...</p>}

      {casesQuery.isError && (
        <p style={{ color: "crimson" }}>
          Failed to load cases: {(casesQuery.error as Error).message}
        </p>
      )}

      {casesQuery.isSuccess && (
        <>
          <p>Total cases: {casesQuery.data.totalCount}</p>

          {casesQuery.data.items.length === 0 ? (
            <p>No cases found.</p>
          ) : (
            <div style={{ overflowX: "auto" }}>
              <table
                style={{
                  width: "100%",
                  borderCollapse: "collapse",
                  marginTop: "16px",
                }}
              >
                <thead>
                  <tr>
                    <th style={thStyle}>Id</th>
                    <th style={thStyle}>Title</th>
                    <th style={thStyle}>Status</th>
                    <th style={thStyle}>Created</th>
                    <th style={thStyle}>Updated</th>
                  </tr>
                </thead>
                <tbody>
                  {casesQuery.data.items.map((item: CasesListItem) => (
                    <tr key={item.id}>
                      <td style={tdStyle}>
                        <Link to={`/cases/${item.id}`}>{item.id}</Link>
                      </td>
                      <td style={tdStyle}>
                        <Link to={`/cases/${item.id}`}>{item.title}</Link>
                      </td>
                      <td style={tdStyle}>{item.status}</td>
                      <td style={tdStyle}>{formatDate(item.createdAtUtc)}</td>
                      <td style={tdStyle}>{formatDate(item.updatedAtUtc)}</td>
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

const thStyle: CSSProperties = {
  textAlign: "left",
  borderBottom: "1px solid #ccc",
  padding: "10px",
};

const tdStyle: CSSProperties = {
  borderBottom: "1px solid #eee",
  padding: "10px",
};

const newCaseLinkStyle: CSSProperties = {
  padding: "10px 16px",
  borderRadius: "8px",
  border: "1px solid #1976d2",
  textDecoration: "none",
  color: "inherit",
  display: "inline-flex",
  alignItems: "center",
};
