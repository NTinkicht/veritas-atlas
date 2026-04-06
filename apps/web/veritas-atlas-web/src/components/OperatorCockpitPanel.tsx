import { Link } from "react-router-dom";

export function OperatorCockpitPanel() {
  return (
    <div style={panelStyle}>
      <h3 style={{ marginTop: 0 }}>Operator Cockpit</h3>
      <div style={{ display: "flex", gap: 12, flexWrap: "wrap" }}>
        <Link to="/case-explorer">Case Explorer</Link>
        <Link to="/truth-review-studio">Truth Review Studio</Link>
        <Link to="/publication-pipeline">Publication Pipeline</Link>
        <Link to="/decision-intelligence">Decision Intelligence</Link>
        <Link to="/release-readiness-hub">Release Readiness Hub</Link>
      </div>
    </div>
  );
}

const panelStyle: React.CSSProperties = {
  border: "1px solid #ddd",
  borderRadius: 14,
  padding: 16,
};