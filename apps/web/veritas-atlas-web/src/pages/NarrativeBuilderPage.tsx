import { Link } from "react-router-dom";
import { useClaims } from "../hooks/useClaims";
import { useContradictions } from "../hooks/useContradictions";

export function NarrativeBuilderPage() {
  const claimsQuery = useClaims();
  const contradictionsQuery = useContradictions();

  const claims = claimsQuery.data?.items ?? [];
  const contradictions = contradictionsQuery.data?.items ?? [];

  return (
    <div style={{ fontFamily: "Arial, sans-serif", padding: 24 }}>
      <header style={{ marginBottom: 24 }}>
        <h1 style={{ margin: 0 }}>Narrative Builder</h1>
        <p style={{ color: "#555" }}>
          Structured drafting surface for building publication-ready narratives from reviewed items.
        </p>

        <nav style={{ display: "flex", gap: 16, marginTop: 12, flexWrap: "wrap" }}>
          <Link to="/publication-pipeline">Publication Pipeline</Link>
          <Link to="/narrative-builder">Narrative Builder</Link>
          <Link to="/decision-log">Decision Log</Link>
        </nav>
      </header>

      <div style={panelStyle}>
        <h3 style={{ marginTop: 0 }}>Narrative Inputs</h3>
        <ul style={{ marginBottom: 0 }}>
          <li>Claims available: {claims.length}</li>
          <li>Contradictions available: {contradictions.length}</li>
          <li>Decision log available: yes</li>
        </ul>
      </div>

      <div style={{ ...panelStyle, marginTop: 20 }}>
        <h3 style={{ marginTop: 0 }}>Draft Structure</h3>
        <ol style={{ marginBottom: 0, paddingLeft: 20 }}>
          <li>Opening claim context</li>
          <li>Evidence trace and supporting statements</li>
          <li>Contradiction analysis</li>
          <li>Decision rationale</li>
          <li>Publication-ready summary</li>
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