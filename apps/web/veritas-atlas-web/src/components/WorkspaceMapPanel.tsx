type WorkspaceMapItem = {
  label: string;
  route: string;
};

export function WorkspaceMapPanel({
  items,
}: {
  items: WorkspaceMapItem[];
}) {
  return (
    <div style={panelStyle}>
      <h3 style={{ marginTop: 0 }}>Workspace Map</h3>
      <ul style={{ marginBottom: 0 }}>
        {items.map((item) => (
          <li key={item.route}>
            {item.label} - {item.route}
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