import { Link } from "react-router-dom";
import { useStatements } from "../hooks/useStatements";

export function StatementsPage() {
  const query = useStatements();

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

      {query.isLoading && <p>Loading statements...</p>}

      {query.isError && (
        <p style={{ color: "crimson" }}>
          Failed to load statements: {(query.error as Error).message}
        </p>
      )}

      {query.isSuccess && (
        <>
          <p>Showing {query.data.items.length} of {query.data.total} statements</p>

          {query.data.items.length === 0 ? (
            <div style={emptyStateStyle}>
              <p style={{ margin: 0 }}>No statements found.</p>
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
                    <th style={thStyle}>Created</th>
                  </tr>
                </thead>
                <tbody>
                  {query.data.items.map((item) => (
                    <tr key={item.id}>
                      <td style={tdStyle}><Link to={`/statements/${item.id}`}>{item.text}</Link></td>
                      <td style={tdStyle}>{item.topic ?? "N/A"}</td>
                      <td style={tdStyle}>{item.polarity}</td>
                      <td style={tdStyle}>{item.status}</td>
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
