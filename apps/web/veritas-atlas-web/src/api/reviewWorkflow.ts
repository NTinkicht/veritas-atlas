import type { WorkflowTransitionResponse } from "./contracts";
import { apiPost } from "./http";

export async function sendClaimToReview(claimId: string): Promise<WorkflowTransitionResponse> {
  return apiPost<WorkflowTransitionResponse>(`/api/v1/review-workflow/claims/${claimId}/send-to-review`);
}

export async function returnClaimForEdit(claimId: string): Promise<WorkflowTransitionResponse> {
  return apiPost<WorkflowTransitionResponse>(`/api/v1/review-workflow/claims/${claimId}/return-for-edit`);
}

export async function escalateContradiction(contradictionId: string): Promise<WorkflowTransitionResponse> {
  return apiPost<WorkflowTransitionResponse>(`/api/v1/review-workflow/contradictions/${contradictionId}/escalate`);
}

export async function reopenReview(reviewId: string): Promise<WorkflowTransitionResponse> {
  return apiPost<WorkflowTransitionResponse>(`/api/v1/review-workflow/reviews/${reviewId}/reopen`);
}