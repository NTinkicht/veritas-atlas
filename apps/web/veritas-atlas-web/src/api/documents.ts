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

export async function getDocuments(): Promise<DocumentsResponse> {
  return apiGet<DocumentsResponse>("/api/v1/documents?page=1&pageSize=50");
}
