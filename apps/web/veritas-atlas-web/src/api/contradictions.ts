import type { ContradictionItem, GetContradictionsResponse } from "./contracts";
import { apiGet } from "./http";

export async function getContradictions(page = 1, pageSize = 20): Promise<GetContradictionsResponse> {
  const params = new URLSearchParams({
    page: String(page),
    pageSize: String(pageSize),
  });

  return apiGet<GetContradictionsResponse>(`/api/v1/contradictions?${params.toString()}`);
}

export async function getContradictionById(id: string): Promise<ContradictionItem> {
  return apiGet<ContradictionItem>(`/api/v1/contradictions/${id}`);
}