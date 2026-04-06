import { Link } from "react-router-dom";
import { AgentActivityPanel } from "../components/AgentActivityPanel";
import { SystemTimelinePanel } from "../components/SystemTimelinePanel";

export function OperationsIntelligencePage() {
  return (
    <div style={{ padding: 24 }}>
      <h1>Operations Intelligence</h1>
      <p>System-wide monitoring of cases, claims, contradictions, and agent activity.</p>

      <div style={{ display: "flex", gap: 12, flexWrap: "wrap", marginBottom: 20 }}>
        <Link to="/dashboard">Dashboard</Link>
        <Link to="/operations">Operations Hub</Link>
        <Link to="/navigator">Navigator</Link>
        <Link to="/claims">Claims</Link>
        <Link to="/contradictions/workspace">Contradictions Workspace</Link>
      </div>

      <section style={{ marginBottom: 24 }}>
        <h2>Live Metrics</h2>
        <ul>
          <li>Total Cases (placeholder)</li>
          <li>Active Investigations (placeholder)</li>
          <li>Pending Reviews (placeholder)</li>
          <li>Agent Runs Today (placeholder)</li>
        </ul>
      </section>

      <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: 16 }}>
        <AgentActivityPanel />
        <SystemTimelinePanel />
      </div>

      <section style={{ marginTop: 24 }}>
        <h2>Activity Streams</h2>
        <p>Recent claims, contradictions, and system events will appear here.</p>
      </section>
    </div>
  );
}