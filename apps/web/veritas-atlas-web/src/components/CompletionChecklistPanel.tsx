type ChecklistItem = {
  label: string;
  status: string;
};

export function CompletionChecklistPanel({
  items,
}: {
  items: ChecklistItem[];
}) {
  return (
    <div style={panelStyle}>
      <h3 style={{ marginTop: 0 }}>Completion Checklist</h3>
      <ul style={{ marginBottom: 0 }}>
        {items.map((item) => (
          <li key={item.label}>
            {item.label} - {item.status}
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