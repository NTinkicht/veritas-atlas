import { Link } from "react-router-dom";
import { useCaseExplorer } from "../hooks/useCaseExplorer";
import { useClaims } from "../hooks/useClaims";
import { useContradictions } from "../hooks/useContradictions";

export function CaseScoreboardPage() {
  const casesQuery = useCaseExplorer();
  const claimsQuery = useClaims();
  const contradictionsQuery = useContradictions();

  const cases = casesQuery.data?.items ?? [];
  const claims = claimsQuery.data?.items ?? [];
  const contradictions = contradictionsQuery.data?.items ?? [];

  return (
    <div style={{ fontFamily: "Arial, sans-serif", padding: 24 }}>
      <header style={{ marginBottom: 24 }}>
        <h1 style={{ margin: 0 }}>Case Scoreboard</h1>
        <p style={{ color: "#555" }}>
          Roll-up scoreboard across cases, claims, and contradictions.
        </p>

        <nav style={{ display: "flex", gap: 16, marginTop: 12, flexWrap: "wrap" }}>
          <Link to="/case-explorer">Case Explorer</Link>
          <Link to="/truth-review-studio">Truth Review Studio</Link>
          <Link to="/case-scoreboard">Case Scoreboard</Link>
        </nav>
      </header>

      <div style={gridStyle}>
        <MetricCard title="Cases" value={cases.length} />
        <MetricCard title="Claims" value={claims.length} />
        <MetricCard title="Contradictions" value={contradictions.length} />
        <MetricCard title="Open Resolution Work" value={contradictions.filter(x => x.status !== "Resolved").length} />
      </div>

      <div style={{ ...panelStyle, marginTop: 20 }}>
        <h3 style={{ marginTop: 0 }}>Top Case Signals</h3>
        {cases.length === 0 && <p>No cases available.</p>}
        {cases.length > 0 && (
          <ul style={{ marginBottom: 0 }}>
            {cases.slice(0, 10).map((item) => (
              <li key={item.id}>
                <Link to={`/case-explorer/${item.id}`}>{item.id}</Link> - {item.status}
              </li>
            ))}
          </ul>
        )}
      </div>
    </div>
  );
}

function MetricCard({ title, value }: { title: string; value: number }) {
  return (
    <div style={metricStyle}>
      <span style={{ color: "#666" }}>{title}</span>
      <strong style={{ fontSize: 28 }}>{value}</strong>
    </div>
  );
}

const gridStyle: React.CSSProperties = {
  display: "grid",
  gridTemplateColumns: "repeat(auto-fit, minmax(180px, 1fr))",
  gap: 16,
};

const metricStyle: React.CSSProperties = {
  border: "1px solid #ddd",
  borderRadius: 14,
  padding: 16,
  display: "grid",
  gap: 8,
};

const panelStyle: React.CSSProperties = {
  border: "1px solid #ddd",
  borderRadius: 14,
  padding: 16,
};