type RouteRegistryItem = {
  category: string;
  count: number;
};

export function RouteRegistryPanel({
  items,
}: {
  items: RouteRegistryItem[];
}) {
  return (
    <div style={panelStyle}>
      <h3 style={{ marginTop: 0 }}>Route Registry</h3>
      <ul style={{ marginBottom: 0 }}>
        {items.map((item) => (
          <li key={item.category}>
            {item.category}: {item.count}
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