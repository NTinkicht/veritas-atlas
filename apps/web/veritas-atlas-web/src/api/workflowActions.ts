export type WorkflowActionResponse = {
  claimId?: string;
  contradictionId?: string;
  reviewId?: string;
  caseId?: string;
  status: string;
  timestamp: string;
};

async function postAction(url: string): Promise<WorkflowActionResponse> {
  const response = await fetch(url, { method: "POST" });

  if (!response.ok) {
    const text = await response.text();
    throw new Error(text || `HTTP ${response.status}`);
  }

  return response.json() as Promise<WorkflowActionResponse>;
}

export function sendClaimToReview(claimId: string) {
  return postAction(`/api/v1/review-workflow/claims/${claimId}/send-to-review`);
}

export function returnClaimForEdit(claimId: string) {
  return postAction(`/api/v1/review-workflow/claims/${claimId}/return-for-edit`);
}

export function escalateContradiction(contradictionId: string) {
  return postAction(`/api/v1/review-workflow/contradictions/${contradictionId}/escalate`);
}

export function reopenReview(reviewId: string) {
  return postAction(`/api/v1/review-workflow/reviews/${reviewId}/reopen`);
}

export function preparePublication(caseId: string) {
  return postAction(`/api/v1/publication-workflow/cases/${caseId}/prepare`);
}

export function publishCase(caseId: string) {
  return postAction(`/api/v1/publication-workflow/cases/${caseId}/publish`);
}

export function holdCase(caseId: string) {
  return postAction(`/api/v1/publication-workflow/cases/${caseId}/hold`);
}