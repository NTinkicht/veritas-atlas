import { apiGet, apiPost } from "./http";
import type { ClaimItem, CreateClaimRequest, PagedResponse } from "./contracts";

export type { CreateClaimRequest };

export async function getClaims(page = 1, pageSize = 20): Promise<PagedResponse<ClaimItem>> {
  return apiGet<PagedResponse<ClaimItem>>(`/api/v1/claims?page=${page}&pageSize=${pageSize}`, false);
}

export async function getClaimById(id: string): Promise<ClaimItem> {
  return apiGet<ClaimItem>(`/api/v1/claims/${id}`, false);
}

export async function createClaim(request: CreateClaimRequest): Promise<ClaimItem> {
  return apiPost<ClaimItem>("/api/v1/claims", request, true);
}