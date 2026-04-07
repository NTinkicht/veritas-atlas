import { useWorkflowValidation } from "../hooks/useWorkflowValidation";

export function RolePolicyPage() {
  const query = useWorkflowValidation();

  if (query.isLoading) {
    return <div style={{ fontFamily: "Arial, sans-serif", padding: 24 }}>Loading role policies...</div>;
  }

  if (query.isError || !query.data) {
    return <div style={{ fontFamily: "Arial, sans-serif", padding: 24, color: "crimson" }}>Failed to load role policies.</div>;
  }

  return (
    <div style={{ fontFamily: "Arial, sans-serif", padding: 24 }}>
      <h1 style={{ marginTop: 0 }}>Role Policy</h1>
      <div style={panelStyle}>
        <ul style={{ marginBottom: 0 }}>
          {Object.entries(query.data.roles).map(([actionName, roles]) => (
            <li key={actionName}>
              <strong>{actionName}</strong>: {roles.join(", ")}
            </li>
          ))}
        </ul>
      </div>
    </div>
  );
}

const panelStyle: React.CSSProperties = {
  border: "1px solid #ddd",
  borderRadius: 14,
  padding: 16,
};