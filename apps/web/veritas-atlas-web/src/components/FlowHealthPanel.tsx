export function FlowHealthPanel({
  evidenceCount,
  statementCount,
  claimCount,
  contradictionCount,
}: {
  evidenceCount: number;
  statementCount: number;
  claimCount: number;
  contradictionCount: number;
}) {
  return (
    <div style={panelStyle}>
      <h3 style={{ marginTop: 0 }}>Flow Health</h3>
      <ul style={{ marginBottom: 0 }}>
        <li>Evidence items: {evidenceCount}</li>
        <li>Statements: {statementCount}</li>
        <li>Claims: {claimCount}</li>
        <li>Contradictions: {contradictionCount}</li>
      </ul>
    </div>
  );
}

const panelStyle: React.CSSProperties = {
  border: "1px solid #ddd",
  borderRadius: 14,
  padding: 16,
};