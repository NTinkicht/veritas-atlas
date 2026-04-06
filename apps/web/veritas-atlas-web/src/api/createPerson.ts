import { API_BASE_URL } from "./client";

export type CreatePersonRequest = {
  displayName: string;
  createdBy?: string;
};

export type CreatePersonResponse = {
  id: string;
  displayName: string;
  createdAtUtc: string;
};

export async function createPerson(request: CreatePersonRequest): Promise<CreatePersonResponse> {
  const response = await fetch(`${API_BASE_URL}/api/v1/persons`, {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
    },
    body: JSON.stringify(request),
  });

  if (!response.ok) {
    throw new Error(`POST /api/v1/persons failed with status ${response.status}`);
  }

  return response.json() as Promise<CreatePersonResponse>;
}
