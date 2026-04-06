import { apiGet } from "./client";

export type CasesListItem = {
  id: string;
  title: string;
  status: string;
  createdAtUtc: string;
  updatedAtUtc: string;
};

export type CasesResponse = {
  items: CasesListItem[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
};

export async function getCases(): Promise<CasesResponse> {
  return apiGet<CasesResponse>("/api/v1/cases?page=1&pageSize=20");
}
