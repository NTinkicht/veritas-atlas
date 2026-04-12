import { apiGet } from "./http";
import type { ContradictionItem, PagedResponse } from "./contracts";

export async function getContradictions(page = 1, pageSize = 20): Promise<PagedResponse<ContradictionItem>> {
  return apiGet<PagedResponse<ContradictionItem>>(`/api/v1/contradictions?page=${page}&pageSize=${pageSize}`, false);
}

export async function getContradictionById(id: string): Promise<ContradictionItem> {
  return apiGet<ContradictionItem>(`/api/v1/contradictions/${id}`, false);
}