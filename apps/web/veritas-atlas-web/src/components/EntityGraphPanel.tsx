type EntityGraphNode = {
  id: string;
  label: string;
  type: string;
};

export function EntityGraphPanel({
  nodes,
}: {
  nodes: EntityGraphNode[];
}) {
  return (
    <div style={panelStyle}>
      <h3 style={{ marginTop: 0 }}>Entity Graph</h3>
      {nodes.length === 0 && <p>No graph nodes available.</p>}
      {nodes.length > 0 && (
        <ul style={{ marginBottom: 0 }}>
          {nodes.map((node) => (
            <li key={node.id}>
              {node.label} - {node.type}
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