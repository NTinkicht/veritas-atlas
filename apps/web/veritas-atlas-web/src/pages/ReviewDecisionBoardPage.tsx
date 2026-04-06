import { Link } from "react-router-dom";
import { useClaims } from "../hooks/useClaims";
import { useContradictions } from "../hooks/useContradictions";

export function ReviewDecisionBoardPage() {
  const claimsQuery = useClaims();
  const contradictionsQuery = useContradictions();

  const claims = claimsQuery.data?.items ?? [];
  const contradictions = contradictionsQuery.data?.items ?? [];

  return (
    <div style={{ fontFamily: "Arial, sans-serif", padding: 24 }}>
      <header style={{ marginBottom: 24 }}>
        <h1 style={{ margin: 0 }}>Review Decision Board</h1>
        <p style={{ color: "#555" }}>
          Operational board for decisions across claims, contradictions, and publication readiness.
        </p>

        <nav style={{ display: "flex", gap: 16, marginTop: 12, flexWrap: "wrap" }}>
          <Link to="/">Home</Link>
          <Link to="/truth-review-studio">Truth Review Studio</Link>
          <Link to="/review-decision-board">Review Decision Board</Link>
          <Link to="/publication-pipeline">Publication Pipeline</Link>
        </nav>
      </header>

      <div style={gridStyle}>
        <Lane
          title="Needs Review"
          items={claims.slice(0, 4).map((x) => ({ id: x.id, label: x.topic, href: `/claims/${x.id}` }))}
        />
        <Lane
          title="Needs Resolution"
          items={contradictions.slice(0, 4).map((x) => ({ id: x.id, label: x.topic, href: `/contradiction-resolution/${x.id}` }))}
        />
        <Lane
          title="Ready for Publication"
          items={claims.slice(4, 8).map((x) => ({ id: x.id, label: x.topic, href: `/claims/${x.id}` }))}
        />
      </div>
    </div>
  );
}

function Lane({
  title,
  items,
}: {
  title: string;
  items: Array<{ id: string; label: string; href: string }>;
}) {
  return (
    <div style={laneStyle}>
      <h3 style={{ marginTop: 0 }}>{title}</h3>
      {items.length === 0 && <p>No items.</p>}
      {items.length > 0 && (
        <ul style={{ marginBottom: 0 }}>
          {items.map((item) => (
            <li key={item.id}>
              <Link to={item.href}>{item.label}</Link>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}

const gridStyle: React.CSSProperties = {
  display: "grid",
  gridTemplateColumns: "repeat(auto-fit, minmax(240px, 1fr))",
  gap: 16,
};

const laneStyle: React.CSSProperties = {
  border: "1px solid #ddd",
  borderRadius: 14,
  padding: 16,
  minHeight: 220,
};