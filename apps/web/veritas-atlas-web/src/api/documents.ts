import { apiGet } from "./client";

export type DocumentItem = {
  id: string;
  sourceId: string;
  title: string;
  status: string;
  createdAtUtc: string;
  updatedAtUtc: string;
};

export type DocumentsResponse = {
  items: DocumentItem[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
};

export type DocumentsQuery = {
  sourceId?: string;
  status?: string;
};

export async function getDocuments(query?: DocumentsQuery): Promise<DocumentsResponse> {
  const params = new URLSearchParams({
    page: "1",
    pageSize: "50",
  });

  if (query?.sourceId) params.set("sourceId", query.sourceId);
  if (query?.status && query.status !== "All") params.set("status", query.status);

  return apiGet<DocumentsResponse>(`/api/v1/documents?${params.toString()}`);
}
