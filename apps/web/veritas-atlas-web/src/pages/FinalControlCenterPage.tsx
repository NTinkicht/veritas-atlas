import { Link } from "react-router-dom";
import { FinalControlSummaryPanel } from "../components/FinalControlSummaryPanel";

export function FinalControlCenterPage() {
  const metrics = [
    { label: "Operational breadth", value: "High" },
    { label: "Core workflows", value: "Implemented" },
    { label: "Governance and publication depth", value: "Partial" },
    { label: "Next focus", value: "Deeper backend + business logic" },
  ];

  return (
    <div style={{ fontFamily: "Arial, sans-serif", padding: 24 }}>
      <header style={{ marginBottom: 24 }}>
        <h1 style={{ margin: 0 }}>Final Control Center</h1>
        <p style={{ color: "#555" }}>
          Final high-level control surface summarizing the current Veritas Atlas state.
        </p>
        <nav style={{ display: "flex", gap: 16, marginTop: 12, flexWrap: "wrap" }}>
          <Link to="/final-control-center">Final Control Center</Link>
          <Link to="/executive-readout-workspace">Executive Readout</Link>
          <Link to="/system-readiness-map">System Readiness Map</Link>
        </nav>
      </header>

      <FinalControlSummaryPanel metrics={metrics} />
    </div>
  );
}