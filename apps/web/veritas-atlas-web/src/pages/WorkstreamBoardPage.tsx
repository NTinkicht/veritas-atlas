import { Link } from "react-router-dom";

export function WorkstreamBoardPage() {
  const lanes = {
    Planning: ["Governance refinement", "Review routing"],
    Active: ["Claim operations", "Contradiction preparation", "Publication readiness"],
    Blocked: ["Escalation policy placeholder"],
    Done: ["Ingestion workspace expansion"]
  };

  return (
    <div style={{ padding: 24, fontFamily: "Arial, sans-serif" }}>
      <h1>Workstream Board</h1>
      <p>Visual organization of operational workstreams and current execution state.</p>

      <div style={{ display: "flex", gap: 12, flexWrap: "wrap", marginBottom: 20 }}>
        <Link to="/delivery-control-tower">Delivery Control Tower</Link>
        <Link to="/executive-overview">Executive Overview</Link>
      </div>

      <div style={gridStyle}>
        {Object.entries(lanes).map(([lane, items]) => (
          <div key={lane} style={laneStyle}>
            <h3 style={{ marginTop: 0 }}>{lane}</h3>
            <ul style={{ marginBottom: 0 }}>
              {items.map((item) => (
                <li key={item}>{item}</li>
              ))}
            </ul>
          </div>
        ))}
      </div>
    </div>
  );
}

const gridStyle: React.CSSProperties = {
  display: "grid",
  gridTemplateColumns: "repeat(auto-fit, minmax(220px, 1fr))",
  gap: 16,
};

const laneStyle: React.CSSProperties = {
  border: "1px solid #ddd",
  borderRadius: 14,
  padding: 16,
  minHeight: 220,
};