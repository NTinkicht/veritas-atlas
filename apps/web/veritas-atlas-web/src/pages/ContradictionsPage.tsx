import { Link, useSearchParams } from "react-router-dom";
import { useContradictions } from "../hooks/useContradictions";

export function ContradictionsPage() {
  const [params] = useSearchParams();
  const claimId = params.get("claimId") ?? undefined;
  const caseId = params.get("caseId") ?? undefined;
  const query = useContradictions(claimId, caseId);

  return (
    <div style={{ fontFamily: "Arial, sans-serif", padding: "24px" }}>
      <header style={{ marginBottom: "24px" }}>
        <h1 style={{ margin: 0 }}>Contradictions</h1>
        <p style={{ color: "#555" }}>Live contradiction catalog from Veritas Atlas API</p>
        <nav style={{ display: "flex", gap: "16px", marginTop: "12px", flexWrap: "wrap" }}>
          <Link to="/">Home</Link>
          <Link to="/claims">Claims</Link>
          <Link to="/contradictions">Contradictions</Link>
          <Link to="/resolution-board">Resolution Board</Link>
          <Link to="/contradictions/workspace">Contradictions Workspace</Link>
        </nav>
      </header>

      {claimId && (
        <div style={hintCardStyle}>
          <strong>Claim scope:</strong> {claimId}
        </div>
      )}

      {caseId && (
        <div style={hintCardStyle}>
          <strong>Case scope:</strong> {caseId}
        </div>
      )}

      {query.isLoading && <p>Loading contradictions...</p>}

      {query.isError && (
        <p style={{ color: "crimson" }}>
          Failed to load contradictions: {(query.error as Error).message}
        </p>
      )}

      {query.isSuccess && (
        <>
          <p>Showing {query.data.items.length} of {query.data.totalCount} contradictions</p>

          {query.data.items.length === 0 ? (
            <div style={emptyStateStyle}>
              <p style={{ margin: 0 }}>No contradictions found.</p>
            </div>
          ) : (
            <div style={{ overflowX: "auto" }}>
              <table style={tableStyle}>
                <thead>
                  <tr>
                    <th style={thStyle}>Topic</th>
                    <th style={thStyle}>Type</th>
                    <th style={thStyle}>Severity</th>
                    <th style={thStyle}>Status</th>
                    <th style={thStyle}>Claims</th>
                  </tr>
                </thead>
                <tbody>
                  {query.data.items.map((item) => (
                    <tr key={item.id}>
                      <td style={tdStyle}><Link to={`/contradictions/${item.id}`}>{item.topic}</Link></td>
                      <td style={tdStyle}>{item.contradictionType}</td>
                      <td style={tdStyle}>{item.severity}</td>
                      <td style={tdStyle}>{item.status}</td>
                      <td style={tdStyle}>
                        <div style={{ display: "flex", gap: "8px", flexWrap: "wrap" }}>
                          <Link to={`/claims/${item.primaryClaimId}`}>Primary</Link>
                          <Link to={`/claims/${item.secondaryClaimId}`}>Secondary</Link>
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