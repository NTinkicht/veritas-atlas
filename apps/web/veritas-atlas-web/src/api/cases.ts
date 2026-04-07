import type { CaseItem, GetCasesResponse } from "./contracts";
import { apiGet } from "./http";

export async function getCases(page = 1, pageSize = 20): Promise<GetCasesResponse> {
  const params = new URLSearchParams({
    page: String(page),
    pageSize: String(pageSize),
  });

  return apiGet<GetCasesResponse>(`/api/v1/cases?${params.toString()}`);
}

export async function getCaseById(id: string): Promise<CaseItem> {
  return apiGet<CaseItem>(`/api/v1/cases/${id}`);
}