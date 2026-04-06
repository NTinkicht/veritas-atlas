type DeliveryCheckpoint = {
  label: string;
  detail: string;
};

export function DeliveryCheckpointPanel({
  items,
}: {
  items: DeliveryCheckpoint[];
}) {
  return (
    <div style={panelStyle}>
      <h3 style={{ marginTop: 0 }}>Delivery Checkpoints</h3>
      <ul style={{ marginBottom: 0 }}>
        {items.map((item) => (
          <li key={item.label}>
            <strong>{item.label}</strong>: {item.detail}
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