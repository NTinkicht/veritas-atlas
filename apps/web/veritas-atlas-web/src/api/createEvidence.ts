import { API_BASE_URL } from "./client";

export type CreateEvidenceRequest = {
  documentId: string;
  quote: string;
  notes?: string;
  createdBy?: string;
};

export type CreateEvidenceResponse = {
  id: string;
  documentId: string;
  quote: string;
  status: string;
  createdAtUtc: string;
  updatedAtUtc: string;
};

export async function createEvidence(
  request: CreateEvidenceRequest
): Promise<CreateEvidenceResponse> {
  const response = await fetch(`${API_BASE_URL}/api/v1/evidence`, {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
    },
    body: JSON.stringify(request),
  });

  if (!response.ok) {
    throw new Error(`POST /api/v1/evidence failed with status ${response.status}`);
  }

  return response.json() as Promise<CreateEvidenceResponse>;
}
