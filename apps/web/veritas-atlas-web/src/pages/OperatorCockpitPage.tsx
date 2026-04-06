import { Link } from "react-router-dom";
import { OperatorCockpitPanel } from "../components/OperatorCockpitPanel";

export function OperatorCockpitPage() {
  return (
    <div style={{ fontFamily: "Arial, sans-serif", padding: 24 }}>
      <header style={{ marginBottom: 24 }}>
        <h1 style={{ margin: 0 }}>Operator Cockpit</h1>
        <p style={{ color: "#555" }}>
          Central operator surface for jumping across all major execution workspaces.
        </p>
        <nav style={{ display: "flex", gap: 16, marginTop: 12, flexWrap: "wrap" }}>
          <Link to="/operator-cockpit">Operator Cockpit</Link>
          <Link to="/ops-coordination-center">Ops Coordination</Link>
          <Link to="/delivery-closeout">Delivery Closeout</Link>
        </nav>
      </header>

      <OperatorCockpitPanel />
    </div>
  );
}