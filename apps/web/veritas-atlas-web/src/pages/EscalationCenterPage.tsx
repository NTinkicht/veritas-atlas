import { Link } from "react-router-dom";

export function EscalationCenterPage() {
  const escalations = [
    "Review bottleneck placeholder",
    "Publication blocker placeholder",
    "Contradiction severity escalation placeholder",
    "Operational routing issue placeholder"
  ];

  return (
    <div style={{ padding: 24, fontFamily: "Arial, sans-serif" }}>
      <h1>Escalation Center</h1>
      <p>Central point for operational blockers, high-risk items, and unresolved workflow issues.</p>

      <div style={{ display: "flex", gap: 12, flexWrap: "wrap", marginBottom: 20 }}>
        <Link to="/delivery-control-tower">Delivery Control Tower</Link>
        <Link to="/executive-overview">Executive Overview</Link>
        <Link to="/governance-console">Governance Console</Link>
      </div>

      <section style={panelStyle}>
        <h2 style={{ marginTop: 0 }}>Current escalations</h2>
        <ul style={{ marginBottom: 0 }}>
          {escalations.map((item, index) => (
            <li key={index}>{item}</li>
          ))}
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