import { apiGet } from "./http";
import type { ClaimItem, CreateClaimRequest, PagedResponse } from "./contracts";

export async function getClaims(page = 1, pageSize = 20): Promise<PagedResponse<ClaimItem>> {
  return apiGet<PagedResponse<ClaimItem>>(`/api/v1/claims?page=${page}&pageSize=${pageSize}`, false);
}

export async function getClaimById(id: string): Promise<ClaimItem> {
  return apiGet<ClaimItem>(`/api/v1/claims/${id}`, false);
}

export async function createClaim(_request: CreateClaimRequest): Promise<ClaimItem> {
  throw new Error("createClaim is not implemented in this bundle.");
}