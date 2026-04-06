import { Link } from "react-router-dom";
import { OpsFinalizationPanel } from "../components/OpsFinalizationPanel";

export function OpsFinalizationWorkspacePage() {
  const items = [
    { label: "Core entity pages", status: "Ready" },
    { label: "Case explorer surfaces", status: "Ready" },
    { label: "Contradiction workflow surfaces", status: "Ready" },
    { label: "Review and publication shells", status: "Ready" },
    { label: "Decision and readiness layers", status: "Ready" },
    { label: "Final business-depth pass", status: "Pending" },
  ];

  return (
    <div style={{ fontFamily: "Arial, sans-serif", padding: 24 }}>
      <header style={{ marginBottom: 24 }}>
        <h1 style={{ margin: 0 }}>Ops Finalization Workspace</h1>
        <p style={{ color: "#555" }}>
          Final surface for consolidating operational completion before deeper backend and business logic passes.
        </p>
        <nav style={{ display: "flex", gap: 16, marginTop: 12, flexWrap: "wrap" }}>
          <Link to="/ops-finalization-workspace">Ops Finalization</Link>
          <Link to="/delivery-closeout">Delivery Closeout</Link>
          <Link to="/release-readiness-hub">Release Readiness Hub</Link>
        </nav>
      </header>

      <OpsFinalizationPanel items={items} />
    </div>
  );
}