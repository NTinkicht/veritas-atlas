import { Link } from "react-router-dom";
import { AnalyticsSummaryPanel } from "../components/AnalyticsSummaryPanel";
import { AgentUtilizationPanel } from "../components/AgentUtilizationPanel";

export function AnalyticsCenterPage() {
  return (
    <div style={{ padding: 24, fontFamily: "Arial, sans-serif" }}>
      <h1>Analytics Center</h1>
      <p>Central analytics surface for operational throughput, review pressure, and AI workload visibility.</p>

      <div style={{ display: "flex", gap: 12, flexWrap: "wrap", marginBottom: 20 }}>
        <Link to="/operations-intelligence">Operations Intelligence</Link>
        <Link to="/executive-overview">Executive Overview</Link>
        <Link to="/agent-runs-board">Agent Runs Board</Link>
        <Link to="/quality-radar">Quality Radar</Link>
        <Link to="/case-flow-map">Case Flow Map</Link>
      </div>

      <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: 16, marginBottom: 24 }}>
        <AnalyticsSummaryPanel />
        <AgentUtilizationPanel />
      </div>

      <section style={panelStyle}>
        <h2 style={{ marginTop: 0 }}>Analytics priorities</h2>
        <ul style={{ marginBottom: 0 }}>
          <li>Surface throughput bottlenecks early</li>
          <li>Track review versus publication readiness balance</li>
          <li>Monitor AI workload concentration and idle capacity</li>
        </ul>
      </section>
    </div>
  );
}

const panelStyle: React.CSSProperties = {
  border: "1px solid #ddd",
  borderRadius: 14,
  padding: 16,
};