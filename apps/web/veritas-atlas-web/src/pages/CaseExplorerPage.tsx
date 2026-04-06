import { Link } from "react-router-dom";
import { useMemo } from "react";
import { useCaseExplorer } from "../hooks/useCaseExplorer";
import { useClaims } from "../hooks/useClaims";
import { useContradictions } from "../hooks/useContradictions";
import { CaseExplorerSummaryPanel } from "../components/CaseExplorerSummaryPanel";

export function CaseExplorerPage() {
  const casesQuery = useCaseExplorer();
  const claimsQuery = useClaims();
  const contradictionsQuery = useContradictions();

  const totalCases = casesQuery.data?.items.length ?? 0;
  const totalClaims = claimsQuery.data?.items.length ?? 0;
  const totalContradictions = contradictionsQuery.data?.items.length ?? 0;

  const openCases = useMemo(() => {
    const items = casesQuery.data?.items ?? [];
    return items.filter((x) => x.status !== "Closed" && x.status !== "Resolved").length;
  }, [casesQuery.data]);

  return (
    <div style={{ fontFamily: "Arial, sans-serif", padding: "24px" }}>
      <header style={{ marginBottom: "24px" }}>
        <h1 style={{ margin: 0 }}>Case Explorer</h1>
        <p style={{ color: "#555" }}>
          Main working surface for cases, claims, and contradictions.
        </p>
        <nav style={{ display: "flex", gap: "16px", marginTop: "12px", flexWrap: "wrap" }}>
          <Link to="/">Home</Link>
          <Link to="/cases">Cases</Link>
          <Link to="/case-explorer">Case Explorer</Link>
          <Link to="/contradictions">Contradictions</Link>
          <Link to="/resolution-board">Resolution Board</Link>
        </nav>
      </header>

      <CaseExplorerSummaryPanel
        totalCases={totalCases}
        openCases={openCases}
        totalClaims={totalClaims}
        totalContradictions={totalContradictions}
      />

      <section style={{ marginTop: 24 }}>
        {casesQuery.isLoading && <p>Loading case explorer...</p>}
        {casesQuery.isError && <p style={{ color: "crimson" }}>Failed to load cases: {(casesQuery.error as Error).message}</p>}

        {casesQuery.isSuccess && (
          <div style={{ overflowX: "auto" }}>
            <table style={tableStyle}>
              <thead>
                <tr>
                  <th style={thStyle}>Case</th>
                  <th style={thStyle}>Status</th>
                  <th style={thStyle}>Created</th>
                  <th style={thStyle}>Workbench</th>
                </tr>
              </thead>
              <tbody>
                {casesQuery.data.items.map((item) => (
                  <tr key={item.id}>
                    <td style={tdStyle}>
                      <Link to={`/cases/${item.id}`}>{item.id}</Link>
                    </td>
                    <td style={tdStyle}>{item.status}</td>
                    <td style={tdStyle}>{new Date(item.createdAtUtc).toLocaleString()}</td>
                    <td style={tdStyle}>
                      <Link to={`/case-explorer/${item.id}`}>Open Workbench</Link>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </section>
    </div>
  );
}

const tableStyle: React.CSSProperties = {
  width: "100%",
  borderCollapse: "collapse",
  marginTop: 16,
};

const thStyle: React.CSSProperties = {
  textAlign: "left",
  borderBottom: "1px solid #ccc",
  padding: 10,
};

const tdStyle: React.CSSProperties = {
  borderBottom: "1px solid #eee",
  padding: 10,
  verticalAlign: "top",
};