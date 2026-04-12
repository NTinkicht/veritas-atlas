import { apiGet, apiPost } from "./http";
import type { PagedResponse, WorkflowAuditEntry } from "./contracts";

export async function getWorkflowAuditEntries(): Promise<PagedResponse<WorkflowAuditEntry>> {
  return apiGet<PagedResponse<WorkflowAuditEntry>>("/api/v1/workflow-audit/entries", true);
}

export async function clearWorkflowAudit(): Promise<{ message: string }> {
  return apiPost<{ message: string }>("/api/v1/workflow-audit/clear", undefined, true);
}