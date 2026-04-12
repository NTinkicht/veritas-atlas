import { apiGet, apiPost } from "./http";
import type { ContradictionItem, PagedResponse } from "./contracts";

export type CreateContradictionRequest = {
  caseId: string;
  leftClaimId: string;
  rightClaimId: string;
  type: string;
  severity: string;
  summary: string;
  rationale?: string;
};

export async function getContradictions(page = 1, pageSize = 20): Promise<PagedResponse<ContradictionItem>> {
  return apiGet<PagedResponse<ContradictionItem>>(`/api/v1/contradictions?page=${page}&pageSize=${pageSize}`, false);
}

export async function getContradictionById(id: string): Promise<ContradictionItem> {
  return apiGet<ContradictionItem>(`/api/v1/contradictions/${id}`, false);
}

export async function createContradiction(request: CreateContradictionRequest): Promise<ContradictionItem> {
  return apiPost<ContradictionItem>("/api/v1/contradictions", request, true);
}