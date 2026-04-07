export function WorkflowDiagnosticsPanel({
  stage,
  actions,
  routes,
}: {
  stage: string;
  actions: string[];
  routes: string[];
}) {
  return (
    <div style={panelStyle}>
      <h3 style={{ marginTop: 0 }}>Workflow Diagnostics</h3>
      <p><strong>Stage:</strong> {stage}</p>

      <div style={gridStyle}>
        <div>
          <h4>Actions</h4>
          <ul style={{ marginBottom: 0 }}>
            {actions.map((action) => (
              <li key={action}>{action}</li>
            ))}
          </ul>
        </div>

        <div>
          <h4>Routes</h4>
          <ul style={{ marginBottom: 0 }}>
            {routes.map((route) => (
              <li key={route}>{route}</li>
            ))}
          </ul>
        </div>
      </div>
    </div>
  );
}

const panelStyle: React.CSSProperties = {
  border: "1px solid #ddd",
  borderRadius: 14,
  padding: 16,
};

const gridStyle: React.CSSProperties = {
  display: "grid",
  gridTemplateColumns: "1fr 1fr",
  gap: 16,
};