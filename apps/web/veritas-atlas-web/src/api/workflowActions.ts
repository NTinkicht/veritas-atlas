import { getAuthHeaders } from "./httpAuth";

const API_BASE_URL = (import.meta.env.VITE_API_BASE_URL ?? "").replace(/\/+$/, "");

export type WorkflowActionResponse = {
  id?: string;
  caseId?: string;
  reviewId?: string;
  contradictionId?: string;
  status?: string;
  message?: string;
};

export type CaseWorkflowAction =
  | "submit"
  | "approve"
  | "reject"
  | "preparePublication"
  | "publish"
  | "hold";

function buildUrl(path: string): string {
  return path.startsWith("http") ? path : `${API_BASE_URL}${path}`;
}

async function parseResponse(response: Response): Promise<WorkflowActionResponse> {
  const text = await response.text();

  if (!text) {
    return { message: response.ok ? "Action completed." : `HTTP ${response.status}` };
  }

  try {
    return JSON.parse(text) as WorkflowActionResponse;
  } catch {
    return { message: text };
  }
}

async function post(path: string): Promise<WorkflowActionResponse> {
  const response = await fetch(buildUrl(path), {
    method: "POST",
    headers: {
      ...getAuthHeaders(),
    },
  });

  const payload = await parseResponse(response);

  if (!response.ok) {
    throw new Error(payload.message || `HTTP ${response.status}`);
  }

  return payload;
}

export async function runCaseWorkflowAction(
  caseId: string,
  action: CaseWorkflowAction,
): Promise<WorkflowActionResponse> {
  switch (action) {
    case "submit":
      return post(`/api/v1/actions/cases/${caseId}/submit`);
    case "approve":
      return post(`/api/v1/actions/cases/${caseId}/approve`);
    case "reject":
      return post(`/api/v1/actions/cases/${caseId}/reject`);
    case "preparePublication":
      return post(`/api/v1/actions/cases/${caseId}/prepare`);
    case "publish":
      return post(`/api/v1/actions/cases/${caseId}/publish`);
    case "hold":
      return post(`/api/v1/actions/cases/${caseId}/hold`);
  }
}

export async function submitCase(caseId: string): Promise<WorkflowActionResponse> {
  return runCaseWorkflowAction(caseId, "submit");
}

export async function approveCase(caseId: string): Promise<WorkflowActionResponse> {
  return runCaseWorkflowAction(caseId, "approve");
}

export async function rejectCase(caseId: string): Promise<WorkflowActionResponse> {
  return runCaseWorkflowAction(caseId, "reject");
}

export async function preparePublication(caseId: string): Promise<WorkflowActionResponse> {
  return runCaseWorkflowAction(caseId, "preparePublication");
}

export async function publishCase(caseId: string): Promise<WorkflowActionResponse> {
  return runCaseWorkflowAction(caseId, "publish");
}

export async function holdCase(caseId: string): Promise<WorkflowActionResponse> {
  return runCaseWorkflowAction(caseId, "hold");
}

export async function sendClaimToReview(claimId: string): Promise<WorkflowActionResponse> {
  return post(`/api/v1/workflow/claims/${claimId}/review`);
}

export async function returnClaimForEdit(claimId: string): Promise<WorkflowActionResponse> {
  return post(`/api/v1/workflow/claims/${claimId}/return`);
}

export async function escalateContradiction(contradictionId: string): Promise<WorkflowActionResponse> {
  return post(`/api/v1/workflow/contradictions/${contradictionId}/escalate`);
}

export async function reopenReview(reviewId: string): Promise<WorkflowActionResponse> {
  return post(`/api/v1/workflow/reviews/${reviewId}/reopen`);
}

export async function clearWorkflowAudit(): Promise<WorkflowActionResponse> {
  return post(`/api/v1/workflow-audit/clear`);
}

export async function resetPersistenceSnapshot(): Promise<WorkflowActionResponse> {
  return post(`/api/v1/persistence/reset`);
}

export function normalizeCaseStatus(status?: string | null): string {
  return (status ?? "").trim().toLowerCase();
}

export function getAllowedCaseActions(status?: string | null): CaseWorkflowAction[] {
  const normalized = normalizeCaseStatus(status);

  if (normalized === "open" || normalized === "draft") {
    return ["submit"];
  }

  if (normalized === "inreview" || normalized === "in_review" || normalized === "in review") {
    return ["approve", "reject", "preparePublication", "hold"];
  }

  if (normalized === "approved") {
    return ["preparePublication", "hold"];
  }

  if (normalized === "readyforpublication" || normalized === "ready_for_publication" || normalized === "ready for publication") {
    return ["publish", "hold"];
  }

  if (normalized === "rejected") {
    return ["submit"];
  }

  if (normalized === "onhold" || normalized === "on_hold" || normalized === "on hold") {
    return ["preparePublication"];
  }

  if (normalized === "published") {
    return [];
  }

  return [];
}