import { IntegrationChecklistPanel } from "../components/IntegrationChecklistPanel";
import { useWorkflowValidation } from "../hooks/useWorkflowValidation";

export function WorkflowValidationPage() {
  const query = useWorkflowValidation();

  if (query.isLoading) {
    return <div style={{ fontFamily: "Arial, sans-serif", padding: 24 }}>Loading workflow validation...</div>;
  }

  if (query.isError) {
    return <div style={{ fontFamily: "Arial, sans-serif", padding: 24, color: "crimson" }}>Failed to load workflow validation rules.</div>;
  }

  return (
    <div style={{ fontFamily: "Arial, sans-serif", padding: 24 }}>
      <h1 style={{ marginTop: 0 }}>Workflow Validation</h1>
      <p style={{ color: "#555" }}>
        Current workflow rules and validation assumptions.
      </p>
      <IntegrationChecklistPanel items={query.data ?? []} />
    </div>
  );
}