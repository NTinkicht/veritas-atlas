import { apiGet } from "./client";

export type SourceItem = {
  id: string;
  name: string;
  type: string;
  reference: string | null;
  status: string;
  createdAtUtc: string;
  updatedAtUtc: string;
};

export type SourcesResponse = {
  items: SourceItem[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
};

export async function getSources(): Promise<SourcesResponse> {
  return apiGet<SourcesResponse>("/api/v1/sources?page=1&pageSize=50");
}
