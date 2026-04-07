type ActionButtonItem = {
  label: string;
  onClick: () => void;
  disabled?: boolean;
};

export function ActionButtonsPanel({
  title,
  items,
  message,
}: {
  title: string;
  items: ActionButtonItem[];
  message?: string;
}) {
  return (
    <div style={panelStyle}>
      <h3 style={{ marginTop: 0 }}>{title}</h3>
      <div style={{ display: "flex", gap: 12, flexWrap: "wrap" }}>
        {items.map((item) => (
          <button
            key={item.label}
            onClick={item.onClick}
            disabled={item.disabled}
            style={buttonStyle}
          >
            {item.label}
          </button>
        ))}
      </div>
      {message && <p style={{ marginTop: 12, marginBottom: 0 }}>{message}</p>}
    </div>
  );
}

const panelStyle: React.CSSProperties = {
  border: "1px solid #ddd",
  borderRadius: 14,
  padding: 16,
};

const buttonStyle: React.CSSProperties = {
  border: "1px solid #bbb",
  borderRadius: 10,
  padding: "10px 14px",
  background: "white",
  cursor: "pointer",
  font: "inherit",
};