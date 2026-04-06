import { API_BASE_URL } from "./client";

export type CreateContradictionRequest = {
  caseId: string;
  leftClaimId: string;
  rightClaimId: string;
  type: string;
  summary: string;
  rationale?: string | null;
  createdBy?: string;
};

export type CreateContradictionResponse = {
  id: string;
  caseId: string;
  leftClaimId: string;
  rightClaimId: string;
  type: string;
  severity: string;
  status: string;
  summary: string;
  rationale: string | null;
  createdAtUtc: string;
  updatedAtUtc: string;
};

export async function createContradiction(
  request: CreateContradictionRequest
): Promise<CreateContradictionResponse> {
  const response = await fetch(`${API_BASE_URL}/api/v1/contradictions`, {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
    },
    body: JSON.stringify(request),
  });

  if (!response.ok) {
    throw new Error(`POST /api/v1/contradictions failed with status ${response.status}`);
  }

  return response.json() as Promise<CreateContradictionResponse>;
}
