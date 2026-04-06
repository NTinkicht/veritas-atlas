import { Link } from "react-router-dom";
import { WorkspaceMapPanel } from "../components/WorkspaceMapPanel";

export function WorkspaceMapPage() {
  const items = [
    { label: "Case Explorer", route: "/case-explorer" },
    { label: "Truth Review Studio", route: "/truth-review-studio" },
    { label: "Publication Pipeline", route: "/publication-pipeline" },
    { label: "Decision Intelligence", route: "/decision-intelligence" },
    { label: "Release Readiness Hub", route: "/release-readiness-hub" },
    { label: "Operator Cockpit", route: "/operator-cockpit" },
  ];

  return (
    <div style={{ fontFamily: "Arial, sans-serif", padding: 24 }}>
      <header style={{ marginBottom: 24 }}>
        <h1 style={{ margin: 0 }}>Workspace Map</h1>
        <p style={{ color: "#555" }}>
          Quick navigation map across the major operational workspaces already present in the product.
        </p>
        <nav style={{ display: "flex", gap: 16, marginTop: 12, flexWrap: "wrap" }}>
          <Link to="/workspace-map">Workspace Map</Link>
          <Link to="/operator-cockpit">Operator Cockpit</Link>
          <Link to="/ops-coordination-center">Ops Coordination</Link>
        </nav>
      </header>

      <WorkspaceMapPanel items={items} />
    </div>
  );
}