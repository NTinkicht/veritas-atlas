type EvidenceTraceNode = {
  id: string;
  label: string;
  detail: string;
};

export function EvidenceTracePanel({
  nodes,
}: {
  nodes: EvidenceTraceNode[];
}) {
  return (
    <div style={panelStyle}>
      <h3 style={{ marginTop: 0 }}>Evidence Trace</h3>
      {nodes.length === 0 && <p>No trace nodes available.</p>}
      {nodes.length > 0 && (
        <ul style={{ marginBottom: 0 }}>
          {nodes.map((node) => (
            <li key={node.id}>
              <strong>{node.label}</strong>: {node.detail}
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}

const panelStyle: React.CSSProperties = {
  border: "1px solid #ddd",
  borderRadius: 14,
  padding: 16,
};