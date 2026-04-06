type OpsCoordinationItem = {
  workspace: string;
  nextAction: string;
};

export function OpsCoordinationMatrixPanel({
  items,
}: {
  items: OpsCoordinationItem[];
}) {
  return (
    <div style={panelStyle}>
      <h3 style={{ marginTop: 0 }}>Ops Coordination Matrix</h3>
      <ul style={{ marginBottom: 0 }}>
        {items.map((item) => (
          <li key={item.workspace}>
            {item.workspace} - {item.nextAction}
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