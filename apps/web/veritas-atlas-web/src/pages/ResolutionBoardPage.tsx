import { Link } from "react-router-dom";
import { useContradictions } from "../hooks/useContradictions";

export function ResolutionBoardPage() {
  const query = useContradictions();

  const items = query.data?.items ?? [];
  const lanes = {
    Draft: items.filter(x => x.status === "Draft"),
    Active: items.filter(x => x.status === "Active"),
    Resolved: items.filter(x => x.status === "Resolved")
  };

  return (
    <div style={{ padding: 24, fontFamily: "Arial, sans-serif" }}>
      <h1>Resolution Board</h1>
      <p>Operational board for contradiction handling and resolution flow.</p>

      <div style={{ display: "flex", gap: 12, flexWrap: "wrap", marginBottom: 20 }}>
        <Link to="/contradictions">Contradictions</Link>
        <Link to="/contradictions/workspace">Contradictions Workspace</Link>
        <Link to="/review-queue">Review Queue</Link>
      </div>

      {query.isLoading && <p>Loading contradiction board...</p>}
      {query.isError && <p style={{ color: "crimson" }}>Failed to load contradiction board.</p>}

      {query.isSuccess && (
        <div style={gridStyle}>
          {Object.entries(lanes).map(([lane, laneItems]) => (
            <div key={lane} style={laneStyle}>
              <h3 style={{ marginTop: 0 }}>{lane}</h3>
              {laneItems.length === 0 && <p>No items.</p>}
              {laneItems.length > 0 && (
                <ul style={{ marginBottom: 0 }}>
                  {laneItems.map((item) => (
                    <li key={item.id}>
                      <Link to={`/contradictions/${item.id}`}>{item.topic}</Link> - {item.severity}
                    </li>
                  ))}
                </ul>
              )}
            </div>
          ))}
        </div>
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