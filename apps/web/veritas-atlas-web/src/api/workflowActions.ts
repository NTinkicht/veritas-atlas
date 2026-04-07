import type { WorkflowSeedResponse, WorkflowTransitionResponse } from "./contracts";
import { apiPost } from "./http";

export async function seedLifecycle(): Promise<WorkflowSeedResponse> {
  return apiPost<WorkflowSeedResponse>("/api/v1/actions/seed/lifecycle");
}

export async function submitCase(caseId: string): Promise<WorkflowTransitionResponse> {
  return apiPost<WorkflowTransitionResponse>(`/api/v1/actions/cases/${caseId}/submit`);
}

export async function approveCase(caseId: string): Promise<WorkflowTransitionResponse> {
  return apiPost<WorkflowTransitionResponse>(`/api/v1/actions/cases/${caseId}/approve`);
}

export async function rejectCase(caseId: string): Promise<WorkflowTransitionResponse> {
  return apiPost<WorkflowTransitionResponse>(`/api/v1/actions/cases/${caseId}/reject`);
}

export async function resolveContradiction(contradictionId: string): Promise<WorkflowTransitionResponse> {
  return apiPost<WorkflowTransitionResponse>(`/api/v1/actions/contradictions/${contradictionId}/resolve`);
}

export async function completeReview(reviewId: string): Promise<WorkflowTransitionResponse> {
  return apiPost<WorkflowTransitionResponse>(`/api/v1/actions/reviews/${reviewId}/complete`);
}