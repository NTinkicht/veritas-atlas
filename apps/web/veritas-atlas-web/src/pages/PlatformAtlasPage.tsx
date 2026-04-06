import { Link } from "react-router-dom";
import { PlatformAtlasSummaryPanel } from "../components/PlatformAtlasSummaryPanel";

export function PlatformAtlasPage() {
  const metrics = [
    { label: "Current state", value: "Operational prototype" },
    { label: "Core vertical slices", value: "Claims, contradictions, review shell" },
    { label: "UI surface breadth", value: "High" },
    { label: "Next depth focus", value: "Backend and business logic" },
  ];

  return (
    <div style={{ fontFamily: "Arial, sans-serif", padding: 24 }}>
      <header style={{ marginBottom: 24 }}>
        <h1 style={{ margin: 0 }}>Platform Atlas</h1>
        <p style={{ color: "#555" }}>
          Consolidated picture of the Veritas Atlas platform and its current operational footprint.
        </p>
        <nav style={{ display: "flex", gap: 16, marginTop: 12, flexWrap: "wrap" }}>
          <Link to="/">Home</Link>
          <Link to="/platform-atlas">Platform Atlas</Link>
          <Link to="/system-readiness-map">System Readiness Map</Link>
          <Link to="/executive-readout-workspace">Executive Readout</Link>
        </nav>
      </header>

      <PlatformAtlasSummaryPanel metrics={metrics} />
    </div>
  );
}