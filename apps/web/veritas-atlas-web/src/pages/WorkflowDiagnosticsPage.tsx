import { WorkflowDiagnosticsPanel } from "../components/WorkflowDiagnosticsPanel";
import {
  useWorkflowDiagnosticsRoutes,
  useWorkflowDiagnosticsSummary,
} from "../hooks/useWorkflowDiagnostics";

export function WorkflowDiagnosticsPage() {
  const summaryQuery = useWorkflowDiagnosticsSummary();
  const routesQuery = useWorkflowDiagnosticsRoutes();

  if (summaryQuery.isLoading || routesQuery.isLoading) {
    return <div style={{ fontFamily: "Arial, sans-serif", padding: 24 }}>Loading workflow diagnostics...</div>;
  }

  if (summaryQuery.isError || routesQuery.isError) {
    return <div style={{ fontFamily: "Arial, sans-serif", padding: 24, color: "crimson" }}>Failed to load workflow diagnostics.</div>;
  }

  if (!summaryQuery.data || !routesQuery.data) {
    return <div style={{ fontFamily: "Arial, sans-serif", padding: 24 }}>Workflow diagnostics unavailable.</div>;
  }

  return (
    <div style={{ fontFamily: "Arial, sans-serif", padding: 24 }}>
      <h1 style={{ marginTop: 0 }}>Workflow Diagnostics</h1>
      <p style={{ color: "#555" }}>
        Diagnostics surface for the write-action workflow layer added during Phase 7.
      </p>

      <WorkflowDiagnosticsPanel
        stage={summaryQuery.data.stage}
        actions={summaryQuery.data.actions}
        routes={routesQuery.data}
      />
    </div>
  );
}