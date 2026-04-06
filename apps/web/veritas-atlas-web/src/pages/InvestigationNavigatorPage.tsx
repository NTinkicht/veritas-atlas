import { Link, useSearchParams } from "react-router-dom";
import { useMemo } from "react";
import { useStatements } from "../hooks/useStatements";
import { useClaims } from "../hooks/useClaims";

export function InvestigationNavigatorPage() {
  const [params] = useSearchParams();
  const q = (params.get("q") ?? "").toLowerCase();

  const statementsQuery = useStatements();
  const claimsQuery = useClaims();

  const statementMatches = useMemo(() => {
    const items = statementsQuery.data?.items ?? [];
    if (!q) return items.slice(0, 12);

    return items.filter((item) =>
      item.text.toLowerCase().includes(q) ||
      (item.topic ?? "").toLowerCase().includes(q) ||
      (item.object ?? "").toLowerCase().includes(q) ||
      (item.predicate ?? "").toLowerCase().includes(q)
    ).slice(0, 12);
  }, [statementsQuery.data, q]);

  const claimMatches = useMemo(() => {
    const items = claimsQuery.data?.items ?? [];
    if (!q) return items.slice(0, 12);

    return items.filter((item) =>
      item.topic.toLowerCase().includes(q) ||
      item.normalizedText.toLowerCase().includes(q) ||
      item.type.toLowerCase().includes(q)
    ).slice(0, 12);
  }, [claimsQuery.data, q]);

  return (
    <div style={{ fontFamily: "Arial, sans-serif", padding: "24px" }}>
      <header style={{ marginBottom: "24px" }}>
        <h1 style={{ margin: 0 }}>Investigation Navigator</h1>
        <p style={{ color: "#555" }}>
          Fast cross-navigation between statements, claims, and contradiction preparation.
        </p>
        <nav style={{ display: "flex", gap: "16px", marginTop: "12px", flexWrap: "wrap" }}>
          <Link to="/">Home</Link>
          <Link to="/operations">Operations Hub</Link>
          <Link to="/statements">Statements</Link>
          <Link to="/claims">Claims</Link>
          <Link to="/contradictions/workspace">Contradictions Workspace</Link>
        </nav>
      </header>

      <div style={hintCardStyle}>
        Current deep-link query: <strong>{q || "(none)"}</strong>
      </div>

      <section style={gridStyle}>
        <div style={panelStyle}>
          <h3 style={{ marginTop: 0 }}>Statement matches</h3>
          {statementsQuery.isLoading && <p>Loading statements...</p>}
          {statementsQuery.isError && <p style={{ color: "crimson" }}>Failed to load statements.</p>}
          {statementsQuery.isSuccess && statementMatches.length === 0 && <p>No matching statements.</p>}
          {statementsQuery.isSuccess && statementMatches.length > 0 && (
            <ul style={{ marginBottom: 0 }}>
              {statementMatches.map((item) => (
                <li key={item.id}>
                  <Link to={`/statements/${item.id}`}>{item.text}</Link>
                </li>
              ))}
            </ul>
          )}
        </div>

        <div style={panelStyle}>
          <h3 style={{ marginTop: 0 }}>Claim matches</h3>
          {claimsQuery.isLoading && <p>Loading claims...</p>}
          {claimsQuery.isError && <p style={{ color: "crimson" }}>Failed to load claims.</p>}
          {claimsQuery.isSuccess && claimMatches.length === 0 && <p>No matching claims.</p>}
          {claimsQuery.isSuccess && claimMatches.length > 0 && (
            <ul style={{ marginBottom: 0 }}>
              {claimMatches.map((item) => (
                <li key={item.id}>
                  <Link to={`/claims/${item.id}`}>{item.topic}</Link> - {item.type}
                </li>
              ))}
            </ul>
          )}
        </div>
      </section>
    </div>
  );
}

const gridStyle: React.CSSProperties = {
  display: "grid",
  gridTemplateColumns: "repeat(auto-fit, minmax(320px, 1fr))",
  gap: "16px",
};

const panelStyle: React.CSSProperties = {
  border: "1px solid #ddd",
  borderRadius: "14px",
  padding: "16px",
};

const hintCardStyle: React.CSSProperties = {
  marginBottom: "16px",
  padding: "12px 14px",
  border: "1px solid #d9e6ff",
  borderRadius: "12px",
  background: "#f8fbff",
};
