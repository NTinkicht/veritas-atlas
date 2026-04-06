import { Link } from "react-router-dom";
import { PublicationChecklistPanel } from "../components/PublicationChecklistPanel";

export function PublicationPipelinePage() {
  const checklist = [
    { label: "Claim reviewed", done: true },
    { label: "Contradictions reviewed", done: true },
    { label: "Decision logged", done: false },
    { label: "Publication narrative prepared", done: false },
    { label: "Final publication routing", done: false },
  ];

  return (
    <div style={{ fontFamily: "Arial, sans-serif", padding: 24 }}>
      <header style={{ marginBottom: 24 }}>
        <h1 style={{ margin: 0 }}>Publication Pipeline</h1>
        <p style={{ color: "#555" }}>
          Working surface for publication preparation, gating, and final release flow.
        </p>

        <nav style={{ display: "flex", gap: 16, marginTop: 12, flexWrap: "wrap" }}>
          <Link to="/review-decision-board">Review Decision Board</Link>
          <Link to="/publication-pipeline">Publication Pipeline</Link>
          <Link to="/narrative-builder">Narrative Builder</Link>
          <Link to="/publication-desk">Publication Desk</Link>
        </nav>
      </header>

      <PublicationChecklistPanel items={checklist} />

      <div style={{ ...panelStyle, marginTop: 20 }}>
        <h3 style={{ marginTop: 0 }}>Pipeline Stages</h3>
        <ol style={{ marginBottom: 0, paddingLeft: 20 }}>
          <li>Review complete</li>
          <li>Contradiction resolution complete</li>
          <li>Decision logged</li>
          <li>Narrative drafted</li>
          <li>Publication desk handoff</li>
        </ol>
      </div>
    </div>
  );
}

const panelStyle: React.CSSProperties = {
  border: "1px solid #ddd",
  borderRadius: 14,
  padding: 16,
};