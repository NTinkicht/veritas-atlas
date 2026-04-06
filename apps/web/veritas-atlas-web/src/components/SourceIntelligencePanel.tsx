type SourceIntelligenceItem = {
  id: string;
  name: string;
  status: string;
};

export function SourceIntelligencePanel({
  items,
}: {
  items: SourceIntelligenceItem[];
}) {
  return (
    <div style={panelStyle}>
      <h3 style={{ marginTop: 0 }}>Source Intelligence</h3>
      {items.length === 0 && <p>No sources available.</p>}
      {items.length > 0 && (
        <ul style={{ marginBottom: 0 }}>
          {items.map((item) => (
            <li key={item.id}>
              {item.name} - {item.status}
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