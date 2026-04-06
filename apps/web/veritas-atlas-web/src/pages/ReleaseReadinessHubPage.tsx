import { Link } from "react-router-dom";
import { ReleaseReadinessPanel } from "../components/ReleaseReadinessPanel";

export function ReleaseReadinessHubPage() {
  const items = [
    { label: "Claims workflow", status: "Ready" },
    { label: "Contradiction workflow", status: "Ready" },
    { label: "Review surfaces", status: "Ready" },
    { label: "Publication governance", status: "In progress" },
    { label: "Narrative layer", status: "In progress" },
  ];

  return (
    <div style={{ fontFamily: "Arial, sans-serif", padding: 24 }}>
      <header style={{ marginBottom: 24 }}>
        <h1 style={{ margin: 0 }}>Release Readiness Hub</h1>
        <p style={{ color: "#555" }}>
          Central release-readiness surface across operational, review, and publication layers.
        </p>
        <nav style={{ display: "flex", gap: 16, marginTop: 12, flexWrap: "wrap" }}>
          <Link to="/">Home</Link>
          <Link to="/release-readiness-hub">Release Readiness Hub</Link>
          <Link to="/publication-governance">Publication Governance</Link>
          <Link to="/readiness-radar-workspace">Readiness Radar</Link>
        </nav>
      </header>

      <ReleaseReadinessPanel items={items} />
    </div>
  );
}