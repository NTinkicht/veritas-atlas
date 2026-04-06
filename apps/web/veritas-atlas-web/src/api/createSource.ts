import { API_BASE_URL } from "./client";

export type CreateSourceRequest = {
  name: string;
  type: string;
  reference?: string;
  createdBy?: string;
};

export type CreateSourceResponse = {
  id: string;
  name: string;
  type: string;
  reference: string | null;
  status: string;
  createdAtUtc: string;
  updatedAtUtc: string;
};

export async function createSource(
  request: CreateSourceRequest
): Promise<CreateSourceResponse> {
  const response = await fetch(`${API_BASE_URL}/api/v1/sources`, {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
    },
    body: JSON.stringify(request),
  });

  if (!response.ok) {
    throw new Error(`POST /api/v1/sources failed with status ${response.status}`);
  }

  return response.json() as Promise<CreateSourceResponse>;
}
