type ExecutiveReadoutItem = {
  label: string;
  value: string;
};

export function ExecutiveReadoutPanel({
  items,
}: {
  items: ExecutiveReadoutItem[];
}) {
  return (
    <div style={panelStyle}>
      <h3 style={{ marginTop: 0 }}>Executive Readout</h3>
      <ul style={{ marginBottom: 0 }}>
        {items.map((item) => (
          <li key={item.label}>
            <strong>{item.label}</strong>: {item.value}
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