import { Link } from "react-router-dom";

export function AgentRunsBoardPage() {
  const runs = [
    { name: "Extraction Agent", status: "Idle", detail: "Awaiting new evidence" },
    { name: "Contradiction Agent", status: "Running", detail: "Comparing active claims" },
    { name: "Confidence Agent", status: "Idle", detail: "No pending recalculations" },
    { name: "Review Support Agent", status: "Placeholder", detail: "Future workflow expansion" }
  ];

  return (
    <div style={{ padding: 24, fontFamily: "Arial, sans-serif" }}>
      <h1>Agent Runs Board</h1>
      <p>Board view for current and upcoming AI operations across the system.</p>

      <div style={{ display: "flex", gap: 12, flexWrap: "wrap", marginBottom: 20 }}>
        <Link to="/analytics-center">Analytics Center</Link>
        <Link to="/operations-intelligence">Operations Intelligence</Link>
      </div>

      <div style={gridStyle}>
        {runs.map((run) => (
          <div key={run.name} style={cardStyle}>
            <h3 style={{ marginTop: 0 }}>{run.name}</h3>
            <p><strong>Status:</strong> {run.status}</p>
            <p style={{ marginBottom: 0 }}>{run.detail}</p>
          </div>
        ))}
      </div>
    </div>
  );
}

const gridStyle: React.CSSProperties = {
  display: "grid",
  gridTemplateColumns: "repeat(auto-fit, minmax(240px, 1fr))",
  gap: 16,
};

const cardStyle: React.CSSProperties = {
  border: "1px solid #ddd",
  borderRadius: 14,
  padding: 16,
};