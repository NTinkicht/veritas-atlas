export type WorkflowDiagnosticsSummary = {
  actions: string[];
  stage: string;
  timestamp: string;
};

export async function getWorkflowDiagnosticsSummary(): Promise<WorkflowDiagnosticsSummary> {
  const response = await fetch("/api/v1/workflow-diagnostics/summary");

  if (!response.ok) {
    const text = await response.text();
    throw new Error(text || `HTTP ${response.status}`);
  }

  return response.json() as Promise<WorkflowDiagnosticsSummary>;
}

export async function getWorkflowDiagnosticsRoutes(): Promise<string[]> {
  const response = await fetch("/api/v1/workflow-diagnostics/routes");

  if (!response.ok) {
    const text = await response.text();
    throw new Error(text || `HTTP ${response.status}`);
  }

  return response.json() as Promise<string[]>;
}