type ReadinessNode = {
  label: string;
  status: string;
};

export function SystemReadinessMapPanel({
  nodes,
}: {
  nodes: ReadinessNode[];
}) {
  return (
    <div style={panelStyle}>
      <h3 style={{ marginTop: 0 }}>System Readiness Map</h3>
      <ul style={{ marginBottom: 0 }}>
        {nodes.map((node) => (
          <li key={node.label}>
            {node.label} - {node.status}
          </li>
        ))}
      </ul>
    </div>
  );
}

const panelStyle: React.CSSProperties = {
  border: "1px solid #ddd",
  borderRadius: 14,
  padding: 16,
};