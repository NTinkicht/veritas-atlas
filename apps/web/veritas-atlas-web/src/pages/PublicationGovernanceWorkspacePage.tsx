import { Link } from "react-router-dom";
import { PublicationGovernancePanel } from "../components/PublicationGovernancePanel";

export function PublicationGovernanceWorkspacePage() {
  const checks = [
    { label: "Review outcome documented", status: "Ready" },
    { label: "Contradiction workflow completed", status: "Ready" },
    { label: "Narrative prepared", status: "Pending" },
    { label: "Decision logged", status: "Pending" },
  ];

  return (
    <div style={{ fontFamily: "Arial, sans-serif", padding: 24 }}>
      <header style={{ marginBottom: 24 }}>
        <h1 style={{ margin: 0 }}>Publication Governance Workspace</h1>
        <p style={{ color: "#555" }}>
          Final governance surface before publication routing and release.
        </p>
        <nav style={{ display: "flex", gap: 16, marginTop: 12, flexWrap: "wrap" }}>
          <Link to="/publication-governance">Publication Governance</Link>
          <Link to="/publication-pipeline">Publication Pipeline</Link>
          <Link to="/decision-log">Decision Log</Link>
        </nav>
      </header>

      <PublicationGovernancePanel checks={checks} />
    </div>
  );
}