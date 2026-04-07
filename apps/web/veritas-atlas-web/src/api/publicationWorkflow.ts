import type { WorkflowTransitionResponse } from "./contracts";
import { apiPost } from "./http";

export async function preparePublication(caseId: string): Promise<WorkflowTransitionResponse> {
  return apiPost<WorkflowTransitionResponse>(`/api/v1/publication-workflow/cases/${caseId}/prepare`);
}

export async function publishCase(caseId: string): Promise<WorkflowTransitionResponse> {
  return apiPost<WorkflowTransitionResponse>(`/api/v1/publication-workflow/cases/${caseId}/publish`);
}

export async function holdCase(caseId: string): Promise<WorkflowTransitionResponse> {
  return apiPost<WorkflowTransitionResponse>(`/api/v1/publication-workflow/cases/${caseId}/hold`);
}