import { useMemo, useState } from "react";
import { Link } from "react-router-dom";
import { useClaims } from "../hooks/useClaims";
import { useStatements } from "../hooks/useStatements";
import { useContradictions } from "../hooks/useContradictions";

export function UnifiedSearchWorkspacePage() {
  const [query, setQuery] = useState("");
  const claimsQuery = useClaims();
  const statementsQuery = useStatements();
  const contradictionsQuery = useContradictions();

  const normalized = query.trim().toLowerCase();

  const claimResults = useMemo(() => {
    const items = claimsQuery.data?.items ?? [];
    if (!normalized) return items.slice(0, 8);
    return items.filter((x) =>
      x.topic.toLowerCase().includes(normalized) ||
      x.normalizedText.toLowerCase().includes(normalized)
    ).slice(0, 8);
  }, [claimsQuery.data, normalized]);

  const statementResults = useMemo(() => {
    const items = statementsQuery.data?.items ?? [];
    if (!normalized) return items.slice(0, 8);
    return items.filter((x) =>
      x.text.toLowerCase().includes(normalized) ||
      (x.topic ?? "").toLowerCase().includes(normalized)
    ).slice(0, 8);
  }, [statementsQuery.data, normalized]);

  const contradictionResults = useMemo(() => {
    const items = contradictionsQuery.data?.items ?? [];
    if (!normalized) return items.slice(0, 8);
    return items.filter((x) =>
      x.topic.toLowerCase().includes(normalized) ||
      x.summary.toLowerCase().includes(normalized)
    ).slice(0, 8);
  }, [contradictionsQuery.data, normalized]);

  return (
    <div style={{ fontFamily: "Arial, sans-serif", padding: 24 }}>
      <header style={{ marginBottom: 24 }}>
        <h1 style={{ margin: 0 }}>Unified Search Workspace</h1>
        <p style={{ color: "#555" }}>
          Cross-search statements, claims, and contradictions from one operational surface.
        </p>
        <nav style={{ display: "flex", gap: 16, marginTop: 12, flexWrap: "wrap" }}>
          <Link to="/">Home</Link>
          <Link to="/global-search-workspace">Global Search Workspace</Link>
          <Link to="/truth-review-studio">Truth Review Studio</Link>
          <Link to="/case-explorer">Case Explorer</Link>
        </nav>
      </header>

      <input
        value={query}
        onChange={(e) => setQuery(e.target.value)}
        placeholder="Search across claims, statements, contradictions..."
        style={inputStyle}
      />

      <div style={gridStyle}>
        <ResultPanel title="Claims" emptyText="No claim results.">
          {claimResults.map((item) => (
            <li key={item.id}>
              <Link to={`/claims/${item.id}`}>{item.topic}</Link> - {item.status}
            </li>
          ))}
        </ResultPanel>

        <ResultPanel title="Statements" emptyText="No statement results.">
          {statementResults.map((item) => (
            <li key={item.id}>
              <Link to={`/statements/${item.id}`}>{item.text}</Link>
            </li>
          ))}
        </ResultPanel>

        <ResultPanel title="Contradictions" emptyText="No contradiction results.">
          {contradictionResults.map((item) => (
            <li key={item.id}>
              <Link to={`/contradictions/${item.id}`}>{item.topic}</Link> - {item.status}
            </li>
          ))}
        </ResultPanel>
      </div>
    </div>
  );
}

function ResultPanel({
  title,
  emptyText,
  children,
}: {
  title: string;
  emptyText: string;
  children: React.ReactNode;
}) {
  const count = Array.isArray(children) ? children.length : 0;

  return (
    <div style={panelStyle}>
      <h3 style={{ marginTop: 0 }}>{title}</h3>
      {count === 0 ? <p>{emptyText}</p> : <ul style={{ marginBottom: 0 }}>{children}</ul>}
    </div>
  );
}

const inputStyle: React.CSSProperties = {
  width: "100%",
  boxSizing: "border-box",
  padding: "12px 14px",
  borderRadius: 10,
  border: "1px solid #ccc",
  font: "inherit",
  marginBottom: 20,
};

const gridStyle: React.CSSProperties = {
  display: "grid",
  gridTemplateColumns: "repeat(auto-fit, minmax(280px, 1fr))",
  gap: 16,
};

const panelStyle: React.CSSProperties = {
  border: "1px solid #ddd",
  borderRadius: 14,
  padding: 16,
};