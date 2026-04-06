import { Link } from "react-router-dom";
import { OpsCoordinationMatrixPanel } from "../components/OpsCoordinationMatrixPanel";

export function OpsCoordinationCenterPage() {
  const items = [
    { workspace: "Evidence Flow Studio", nextAction: "Advance evidence into statements and claims" },
    { workspace: "Truth Review Studio", nextAction: "Validate contradictions and review readiness" },
    { workspace: "Publication Pipeline", nextAction: "Prepare narrative and governance checks" },
    { workspace: "Decision Intelligence", nextAction: "Review confidence and explanation surfaces" },
    { workspace: "Release Readiness Hub", nextAction: "Track final release state" },
  ];

  return (
    <div style={{ fontFamily: "Arial, sans-serif", padding: 24 }}>
      <header style={{ marginBottom: 24 }}>
        <h1 style={{ margin: 0 }}>Ops Coordination Center</h1>
        <p style={{ color: "#555" }}>
          Coordination view for moving work cleanly between operational surfaces.
        </p>
        <nav style={{ display: "flex", gap: 16, marginTop: 12, flexWrap: "wrap" }}>
          <Link to="/ops-coordination-center">Ops Coordination Center</Link>
          <Link to="/operational-handoff">Operational Handoff</Link>
          <Link to="/release-readiness-hub">Release Readiness Hub</Link>
        </nav>
      </header>

      <OpsCoordinationMatrixPanel items={items} />
    </div>
  );
}