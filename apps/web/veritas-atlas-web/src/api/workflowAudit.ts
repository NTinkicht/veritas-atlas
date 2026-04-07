export type WorkflowAuditEntry = {
  id: string;
  entityType: string;
  entityId: string;
  actionName: string;
  previousStatus?: string | null;
  nextStatus: string;
  role: string;
  success: boolean;
  message: string;
  timestampUtc: string;
};

export async function getWorkflowAuditEntries(): Promise<WorkflowAuditEntry[]> {
  const response = await fetch("/api/v1/workflow-audit/entries");

  if (!response.ok) {
    const text = await response.text();
    throw new Error(text || `HTTP ${response.status}`);
  }

  return response.json() as Promise<WorkflowAuditEntry[]>;
}

export async function clearWorkflowAudit(): Promise<void> {
  const response = await fetch("/api/v1/workflow-audit/clear", { method: "POST" });

  if (!response.ok) {
    const text = await response.text();
    throw new Error(text || `HTTP ${response.status}`);
  }
}