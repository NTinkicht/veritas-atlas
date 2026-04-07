type WorkflowActionButton = {
  label: string;
  onClick: () => void;
  disabled?: boolean;
};

export function WorkflowActionPanel({
  title,
  buttons,
  message,
}: {
  title: string;
  buttons: WorkflowActionButton[];
  message?: string;
}) {
  return (
    <div style={panelStyle}>
      <h3 style={{ marginTop: 0 }}>{title}</h3>
      <div style={{ display: "flex", gap: 12, flexWrap: "wrap" }}>
        {buttons.map((button) => (
          <button
            key={button.label}
            onClick={button.onClick}
            disabled={button.disabled}
            style={buttonStyle}
          >
            {button.label}
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