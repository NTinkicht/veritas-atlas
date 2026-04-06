import { API_BASE_URL } from "./client";

export type ReviewDecision = "Approved" | "Rejected" | "NeedsChanges";

type CompleteReviewRequest = {
  decision: ReviewDecision;
  reviewer?: string;
  notes?: string;
};

async function completeReview(id: string, body: CompleteReviewRequest): Promise<void> {
  const response = await fetch(`${API_BASE_URL}/api/v1/reviews/${id}/complete`, {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
    },
    body: JSON.stringify(body),
  });

  if (!response.ok) {
    throw new Error(`POST /api/v1/reviews/${id}/complete failed with status ${response.status}`);
  }
}

export async function approveReview(id: string, reviewer = "frontend-reviewer"): Promise<void> {
  return completeReview(id, {
    decision: "Approved",
    reviewer,
    notes: "Approved from frontend",
  });
}

export async function rejectReview(id: string, reviewer = "frontend-reviewer"): Promise<void> {
  return completeReview(id, {
    decision: "Rejected",
    reviewer,
    notes: "Rejected from frontend",
  });
}

export async function requestReviewChanges(id: string, reviewer = "frontend-reviewer"): Promise<void> {
  return completeReview(id, {
    decision: "NeedsChanges",
    reviewer,
    notes: "Needs changes from frontend",
  });
}
