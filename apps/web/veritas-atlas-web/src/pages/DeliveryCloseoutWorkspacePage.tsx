import { Link } from "react-router-dom";
import { DeliveryCheckpointPanel } from "../components/DeliveryCheckpointPanel";

export function DeliveryCloseoutWorkspacePage() {
  const items = [
    { label: "Core entity workflows", detail: "Operational" },
    { label: "Case explorer and workbench", detail: "Operational" },
    { label: "Review and publication pack", detail: "Operational shell ready" },
    { label: "Decision intelligence layer", detail: "Operational shell ready" },
    { label: "Release governance", detail: "Needs final business wiring" },
  ];

  return (
    <div style={{ fontFamily: "Arial, sans-serif", padding: 24 }}>
      <header style={{ marginBottom: 24 }}>
        <h1 style={{ margin: 0 }}>Delivery Closeout Workspace</h1>
        <p style={{ color: "#555" }}>
          Workspace for tracking implementation closeout and remaining readiness gaps.
        </p>
        <nav style={{ display: "flex", gap: 16, marginTop: 12, flexWrap: "wrap" }}>
          <Link to="/delivery-closeout">Delivery Closeout</Link>
          <Link to="/delivery-control-tower">Delivery Control Tower</Link>
          <Link to="/release-readiness-hub">Release Readiness Hub</Link>
        </nav>
      </header>

      <DeliveryCheckpointPanel items={items} />
    </div>
  );
}