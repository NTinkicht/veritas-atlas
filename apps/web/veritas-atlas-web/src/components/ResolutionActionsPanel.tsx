import { Link } from "react-router-dom";

export function ResolutionActionsPanel({
  contradictionId,
  caseId,
}: {
  contradictionId?: string;
  caseId?: string;
}) {
  return (
    <div style={panelStyle}>
      <h3 style={{ marginTop: 0 }}>Resolution Actions</h3>
      <div style={{ display: "flex", gap: 12, flexWrap: "wrap" }}>
        {contradictionId && <Link to={`/contradictions/${contradictionId}`}>Open Contradiction</Link>}
        {caseId && <Link to={`/case-explorer/${caseId}`}>Open Case Workbench</Link>}
        <Link to="/review-queue">Send to Review Queue</Link>
        <Link to="/publication-desk">Open Publication Desk</Link>
        <Link to="/resolution-board">Open Resolution Board</Link>
      </div>
    </div>
  );
}

const panelStyle: React.CSSProperties = {
  border: "1px solid #ddd",
  borderRadius: 14,
  padding: 16,
};