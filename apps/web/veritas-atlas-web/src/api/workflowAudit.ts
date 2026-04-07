import type { PagedResponse, WorkflowAuditEntry, WorkflowAuditResponse } from "./contracts";
import { apiGet } from "./http";

function normalizeAuditResponse(data: WorkflowAuditResponse): PagedResponse<WorkflowAuditEntry> {
  if (Array.isArray(data)) {
    return {
      items: data,
      page: 1,
      pageSize: data.length,
      totalCount: data.length,
      totalPages: 1,
    };
  }

  return data;
}

export async function getWorkflowAuditEntries(): Promise<PagedResponse<WorkflowAuditEntry>> {
  const data = await apiGet<WorkflowAuditResponse>("/api/v1/workflow-audit/entries");
  return normalizeAuditResponse(data);
}