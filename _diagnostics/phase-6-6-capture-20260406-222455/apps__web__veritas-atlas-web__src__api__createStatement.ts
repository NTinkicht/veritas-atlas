import { API_BASE_URL } from "./client";

export type CreateStatementRequest = {
  evidenceId: string;
  text: string;
  createdBy?: string;
};

export type CreateStatementResponse = {
  id: string;
  evidenceId: string;
  documentId: string | null;
  text: string;
  polarity: string;
  status: string;
  topic: string | null;
  createdAtUtc: string;
  updatedAtUtc: string;
};

export async function createStatement(
  request: CreateStatementRequest
): Promise<CreateStatementResponse> {
  const response = await fetch(`${API_BASE_URL}/api/v1/statements`, {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
    },
    body: JSON.stringify(request),
  });

  if (!response.ok) {
    throw new Error(`POST /api/v1/statements failed with status ${response.status}`);
  }

  return response.json() as Promise<CreateStatementResponse>;
}
