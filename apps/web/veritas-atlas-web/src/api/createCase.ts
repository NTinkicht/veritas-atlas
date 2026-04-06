import { API_BASE_URL } from "./client";

export type CreateCaseRequest = {
  title: string;
  summary?: string;
  subjectPersonId?: string | null;
  createdBy?: string;
};

export type CreateCaseResponse = {
  id: string;
  title: string;
  status: string;
  createdAtUtc: string;
};

export async function createCase(request: CreateCaseRequest): Promise<CreateCaseResponse> {
  const response = await fetch(`${API_BASE_URL}/api/v1/cases`, {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
    },
    body: JSON.stringify(request),
  });

  if (!response.ok) {
    throw new Error(`POST /api/v1/cases failed with status ${response.status}`);
  }

  return response.json() as Promise<CreateCaseResponse>;
}
