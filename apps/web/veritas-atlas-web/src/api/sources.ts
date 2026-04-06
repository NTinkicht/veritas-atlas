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

export async function getSources(): Promise<SourcesResponse> {
  return apiGet<SourcesResponse>("/api/v1/sources?page=1&pageSize=50");
}
