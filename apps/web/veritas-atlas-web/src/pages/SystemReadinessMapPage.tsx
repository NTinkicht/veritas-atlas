import { Link } from "react-router-dom";
import { SystemReadinessMapPanel } from "../components/SystemReadinessMapPanel";

export function SystemReadinessMapPage() {
  const nodes = [
    { label: "Core entity workflows", status: "Ready" },
    { label: "Case explorer", status: "Ready" },
    { label: "Contradiction workflow", status: "Ready" },
    { label: "Review surfaces", status: "Ready" },
    { label: "Publication governance", status: "Partial" },
    { label: "Deep business logic", status: "Pending" },
  ];

  return (
    <div style={{ fontFamily: "Arial, sans-serif", padding: 24 }}>
      <header style={{ marginBottom: 24 }}>
        <h1 style={{ margin: 0 }}>System Readiness Map</h1>
        <p style={{ color: "#555" }}>
          Final readiness map across the major Veritas Atlas operational surfaces.
        </p>
        <nav style={{ display: "flex", gap: 16, marginTop: 12, flexWrap: "wrap" }}>
          <Link to="/">Home</Link>
          <Link to="/system-readiness-map">System Readiness Map</Link>
          <Link to="/release-readiness-hub">Release Readiness Hub</Link>
          <Link to="/ops-finalization-workspace">Ops Finalization</Link>
        </nav>
      </header>

      <SystemReadinessMapPanel nodes={nodes} />
    </div>
  );
}