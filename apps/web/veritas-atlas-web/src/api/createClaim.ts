import { API_BASE_URL } from "./client";

export type CreateClaimRequest = {
  statementId: string;
  text: string;
  personId?: string | null;
  createdBy?: string;
};

export type CreateClaimResponse = {
  id: string;
  statementId: string;
  personId: string | null;
  type: string;
  status: string;
  topic: string | null;
  normalizedText: string | null;
  isMaterial: boolean;
  createdAtUtc: string;
  updatedAtUtc: string;
};

export async function createClaim(
  request: CreateClaimRequest
): Promise<CreateClaimResponse> {
  const response = await fetch(`${API_BASE_URL}/api/v1/claims`, {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
    },
    body: JSON.stringify(request),
  });

  if (!response.ok) {
    throw new Error(`POST /api/v1/claims failed with status ${response.status}`);
  }

  return response.json() as Promise<CreateClaimResponse>;
}
