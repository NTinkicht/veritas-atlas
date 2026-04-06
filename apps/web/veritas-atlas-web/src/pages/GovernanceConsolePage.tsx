import { Link } from "react-router-dom";
import { ReviewMetricsPanel } from "../components/ReviewMetricsPanel";
import { PublicationStatusPanel } from "../components/PublicationStatusPanel";

export function GovernanceConsolePage() {
  return (
    <div style={{ padding: 24, fontFamily: "Arial, sans-serif" }}>
      <h1>Governance Console</h1>
      <p>Operational surface for review governance, escalation tracking, and publication readiness.</p>

      <div style={{ display: "flex", gap: 12, flexWrap: "wrap", marginBottom: 20 }}>
        <Link to="/review-queue">Review Queue</Link>
        <Link to="/review-workspace">Review Workspace</Link>
        <Link to="/publication-desk">Publication Desk</Link>
        <Link to="/publication-readiness">Publication Readiness</Link>
        <Link to="/decision-log">Decision Log</Link>
      </div>

      <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: 16, marginBottom: 24 }}>
        <ReviewMetricsPanel />
        <PublicationStatusPanel />
      </div>

      <section style={panelStyle}>
        <h2 style={{ marginTop: 0 }}>Governance priorities</h2>
        <ul style={{ marginBottom: 0 }}>
          <li>Keep publication decisions traceable</li>
          <li>Escalate unresolved contradiction candidates</li>
          <li>Route review-ready items into publication desk</li>
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