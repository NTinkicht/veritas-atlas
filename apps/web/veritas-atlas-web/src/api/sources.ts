import { apiGet } from "./client";

export type SourceReferenceResponse = {
  externalId: string | null;
  url: string | null;
  domain: string | null;
  languageCode: string | null;
};

export type SourceItem = {
  id: string;
  name: string;
  type: string;
  reference: SourceReferenceResponse | null;
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

export type SourcesQuery = {
  search?: string;
  type?: string;
  status?: string;
};

export async function getSources(query?: SourcesQuery): Promise<SourcesResponse> {
  const params = new URLSearchParams({
    page: "1",
    pageSize: "50",
  });

  if (query?.search) params.set("search", query.search);
  if (query?.type && query.type !== "All") params.set("type", query.type);
  if (query?.status && query.status !== "All") params.set("status", query.status);

  return apiGet<SourcesResponse>(`/api/v1/sources?${params.toString()}`);
}
