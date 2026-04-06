type ReviewAuditItem = {
  id: string;
  label: string;
  outcome: string;
};

export function ReviewAuditPanel({
  items,
}: {
  items: ReviewAuditItem[];
}) {
  return (
    <div style={panelStyle}>
      <h3 style={{ marginTop: 0 }}>Review Audit</h3>
      {items.length === 0 && <p>No audit entries available.</p>}
      {items.length > 0 && (
        <ul style={{ marginBottom: 0 }}>
          {items.map((item) => (
            <li key={item.id}>
              {item.label} - {item.outcome}
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