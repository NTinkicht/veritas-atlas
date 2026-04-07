import { useWorkflowValidation } from "../hooks/useWorkflowValidation";

export function WorkflowValidationPage() {
  const query = useWorkflowValidation();

  if (query.isLoading) {
    return <div style={{ fontFamily: "Arial, sans-serif", padding: 24 }}>Loading workflow validation...</div>;
  }

  if (query.isError) {
    return <div style={{ fontFamily: "Arial, sans-serif", padding: 24, color: "crimson" }}>Failed to load workflow validation rules.</div>;
  }

  const payload = query.data;

  if (!payload) {
    return <div style={{ fontFamily: "Arial, sans-serif", padding: 24 }}>No workflow rules available.</div>;
  }

  return (
    <div style={{ fontFamily: "Arial, sans-serif", padding: 24 }}>
      <h1 style={{ marginTop: 0 }}>Workflow Validation</h1>
      <p style={{ color: "#555" }}>
        Transition rules and role policies for the workflow layer.
      </p>

      <div style={gridStyle}>
        <div style={panelStyle}>
          <h3 style={{ marginTop: 0 }}>Transition Rules</h3>
          <ul style={{ marginBottom: 0 }}>
            {Object.entries(payload.transitions).map(([entityType, rules]) => (
              <li key={entityType}>
                <strong>{entityType}</strong>
                <ul>
                  {Object.entries(rules).map(([from, targets]) => (
                    <li key={from}>{from} â†’ {targets.join(", ") || "âˆ…"}</li>
                  ))}
                </ul>
              </li>
            ))}
          </ul>
        </div>

        <div style={panelStyle}>
          <h3 style={{ marginTop: 0 }}>Role Policies</h3>
          <ul style={{ marginBottom: 0 }}>
            {Object.entries(payload.roles).map(([actionName, roles]) => (
              <li key={actionName}>
                <strong>{actionName}</strong>: {roles.join(", ")}
              </li>
            ))}
          </ul>
        </div>
      </div>
    </div>
  );
}

const gridStyle: React.CSSProperties = {
  display: "grid",
  gridTemplateColumns: "1fr 1fr",
  gap: 16,
};

const panelStyle: React.CSSProperties = {
  border: "1px solid #ddd",
  borderRadius: 14,
  padding: 16,
};