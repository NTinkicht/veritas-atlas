import { apiGet, API_BASE_URL } from "./client";

export type PersonDetailResponse = {
  id: string;
  displayName: string;
  description: string | null;
  nationality: string | null;
  createdAtUtc: string;
  updatedAtUtc: string;
  aliases: string[];
};

export type AddAliasRequest = {
  alias: string;
  createdBy?: string;
};

export type AddAliasResponse = {
  id: string;
  personId: string;
  alias: string;
  createdAtUtc: string;
};

export async function getPersonById(id: string): Promise<PersonDetailResponse> {
  return apiGet<PersonDetailResponse>(`/api/v1/persons/${id}`);
}

export async function addAliasToPerson(
  personId: string,
  request: AddAliasRequest
): Promise<AddAliasResponse> {
  const response = await fetch(`${API_BASE_URL}/api/v1/persons/${personId}/aliases`, {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
    },
    body: JSON.stringify(request),
  });

  if (!response.ok) {
    throw new Error(`POST /api/v1/persons/${personId}/aliases failed with status ${response.status}`);
  }

  return response.json() as Promise<AddAliasResponse>;
}
