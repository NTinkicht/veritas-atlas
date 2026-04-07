import { getWorkflowRoleHeaders } from "./workflowRoleContext";

export type ActionResponse = {
  caseId?: string;
  contradictionId?: string;
  reviewId?: string;
  status: string;
  timestampUtc?: string;
  timestamp?: string;
};

async function postAction(url: string): Promise<ActionResponse> {
  const response = await fetch(url, {
    method: "POST",
    headers: getWorkflowRoleHeaders(),
  });

  if (!response.ok) {
    const text = await response.text();
    throw new Error(text || `HTTP ${response.status}`);
  }

  return response.json() as Promise<ActionResponse>;
}

export function submitCase(caseId: string) {
  return postAction(`/api/v1/actions/cases/${caseId}/submit`);
}

export function approveCase(caseId: string) {
  return postAction(`/api/v1/actions/cases/${caseId}/approve`);
}

export function rejectCase(caseId: string) {
  return postAction(`/api/v1/actions/cases/${caseId}/reject`);
}

export function resolveContradiction(contradictionId: string) {
  return postAction(`/api/v1/actions/contradictions/${contradictionId}/resolve`);
}

export function completeReview(reviewId: string) {
  return postAction(`/api/v1/actions/reviews/${reviewId}/complete`);
}