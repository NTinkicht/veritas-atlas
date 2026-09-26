import { apiGet } from "./http";
import type { CaseItem, GetCaseStatusCountsResponse, PagedResponse } from "./contracts";

export async function getCases(page = 1, pageSize = 20): Promise<PagedResponse<CaseItem>> {
  return apiGet<PagedResponse<CaseItem>>(`/api/v1/cases?page=${page}&pageSize=${pageSize}`, false);
}

export async function getCaseById(id: string): Promise<CaseItem> {
  return apiGet<CaseItem>(`/api/v1/cases/${id}`, false);
}

export async function getCaseStatusCounts(): Promise<GetCaseStatusCountsResponse> {
  return apiGet<GetCaseStatusCountsResponse>("/api/v1/cases/status-counts", false);
}
