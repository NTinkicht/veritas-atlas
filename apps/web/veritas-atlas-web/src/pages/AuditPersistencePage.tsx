import { useWorkflowAuditEntries } from "../hooks/useWorkflowAudit";

export function AuditPersistencePage() {
  const query = useWorkflowAuditEntries();

  if (query.isLoading) {
    return <div style={{ fontFamily: "Arial, sans-serif", padding: 24 }}>Loading persisted audit...</div>;
  }

  if (query.isError) {
    return <div style={{ fontFamily: "Arial, sans-serif", padding: 24, color: "crimson" }}>Failed to load persisted audit.</div>;
  }

  return (
    <div style={{ fontFamily: "Arial, sans-serif", padding: 24 }}>
      <h1 style={{ marginTop: 0 }}>Audit Persistence</h1>
      <div style={panelStyle}>
        <p>Persisted entries: {query.data?.length ?? 0}</p>
      </div>
    </div>
  );
}

const panelStyle: React.CSSProperties = {
  border: "1px solid #ddd",
  borderRadius: 14,
  padding: 16,
};