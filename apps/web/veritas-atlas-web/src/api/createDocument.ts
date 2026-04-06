import { API_BASE_URL } from "./client";

export type CreateDocumentRequest = {
  sourceId: string;
  title: string;
  content: string;
  language?: string;
  externalReference?: string;
  createdBy?: string;
};

export type CreateDocumentResponse = {
  id: string;
  sourceId: string;
  title: string;
  status: string;
  createdAtUtc: string;
  updatedAtUtc: string;
};

export async function createDocument(
  request: CreateDocumentRequest
): Promise<CreateDocumentResponse> {
  const response = await fetch(`${API_BASE_URL}/api/v1/documents`, {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
    },
    body: JSON.stringify(request),
  });

  if (!response.ok) {
    throw new Error(`POST /api/v1/documents failed with status ${response.status}`);
  }

  return response.json() as Promise<CreateDocumentResponse>;
}
