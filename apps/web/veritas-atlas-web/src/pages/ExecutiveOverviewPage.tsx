import { Link } from "react-router-dom";
import { ExecutiveSummaryPanel } from "../components/ExecutiveSummaryPanel";
import { DeliveryHealthPanel } from "../components/DeliveryHealthPanel";

export function ExecutiveOverviewPage() {
  return (
    <div style={{ padding: 24, fontFamily: "Arial, sans-serif" }}>
      <h1>Executive Overview</h1>
      <p>High-level operational oversight across governance, review, publication, and delivery execution.</p>

      <div style={{ display: "flex", gap: 12, flexWrap: "wrap", marginBottom: 20 }}>
        <Link to="/operations-intelligence">Operations Intelligence</Link>
        <Link to="/governance-console">Governance Console</Link>
        <Link to="/delivery-control-tower">Delivery Control Tower</Link>
        <Link to="/escalation-center">Escalation Center</Link>
      </div>

      <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: 16, marginBottom: 24 }}>
        <ExecutiveSummaryPanel />
        <DeliveryHealthPanel />
      </div>

      <section style={panelStyle}>
        <h2 style={{ marginTop: 0 }}>Executive priorities</h2>
        <ul style={{ marginBottom: 0 }}>
          <li>Reduce blocked publication items</li>
          <li>Improve review-to-publication flow</li>
          <li>Track unresolved escalations and operational friction</li>
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