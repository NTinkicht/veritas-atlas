import { Link } from "react-router-dom";

export function DeliveryControlTowerPage() {
  return (
    <div style={{ padding: 24, fontFamily: "Arial, sans-serif" }}>
      <h1>Delivery Control Tower</h1>
      <p>Central delivery surface for active workstreams, sequencing, and operational follow-through.</p>

      <div style={{ display: "flex", gap: 12, flexWrap: "wrap", marginBottom: 20 }}>
        <Link to="/executive-overview">Executive Overview</Link>
        <Link to="/workstream-board">Workstream Board</Link>
        <Link to="/escalation-center">Escalation Center</Link>
      </div>

      <section style={panelStyle}>
        <h2 style={{ marginTop: 0 }}>Control priorities</h2>
        <ul style={{ marginBottom: 0 }}>
          <li>Monitor active operational workstreams</li>
          <li>Keep blockers visible</li>
          <li>Route issues to escalation center when needed</li>
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