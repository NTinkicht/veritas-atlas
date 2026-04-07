import { useClearWorkflowAudit, useWorkflowAuditEntries } from "../hooks/useWorkflowAudit";

export function WorkflowAuditPage() {
  const entriesQuery = useWorkflowAuditEntries();
  const clearMutation = useClearWorkflowAudit();

  if (entriesQuery.isLoading) {
    return <div style={{ fontFamily: "Arial, sans-serif", padding: 24 }}>Loading workflow audit...</div>;
  }

  if (entriesQuery.isError) {
    return <div style={{ fontFamily: "Arial, sans-serif", padding: 24, color: "crimson" }}>Failed to load workflow audit.</div>;
  }

  const entries = entriesQuery.data ?? [];

  return (
    <div style={{ fontFamily: "Arial, sans-serif", padding: 24 }}>
      <h1 style={{ marginTop: 0 }}>Workflow Audit</h1>
      <button onClick={() => clearMutation.mutate()} style={buttonStyle}>Clear Audit</button>

      <div style={{ ...panelStyle, marginTop: 16 }}>
        {entries.length === 0 && <p>No audit entries.</p>}
        {entries.length > 0 && (
          <ul style={{ marginBottom: 0 }}>
            {entries.map((entry) => (
              <li key={entry.id}>
                [{entry.timestampUtc}] {entry.entityType} {entry.entityId} - {entry.actionName} - {entry.previousStatus ?? "N/A"} â†’ {entry.nextStatus} - {entry.role} - {entry.success ? "OK" : "FAIL"}
              </li>
            ))}
          </ul>
        )}
      </div>
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