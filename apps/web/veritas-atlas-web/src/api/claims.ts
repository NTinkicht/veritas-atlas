import type { ClaimItem, CreateClaimRequest, GetClaimsResponse } from "./contracts";
import { apiGet, apiPost } from "./http";

export async function createClaim(request: CreateClaimRequest): Promise<ClaimItem> {
  return apiPost<ClaimItem, CreateClaimRequest>("/api/v1/claims", request);
}

export async function getClaims(page = 1, pageSize = 20, statementId?: string): Promise<GetClaimsResponse> {
  const params = new URLSearchParams({
    page: String(page),
    pageSize: String(pageSize),
  });

  if (statementId) {
    params.set("statementId", statementId);
  }

  return apiGet<GetClaimsResponse>(`/api/v1/claims?${params.toString()}`);
}

export async function getClaimById(id: string): Promise<ClaimItem> {
  return apiGet<ClaimItem>(`/api/v1/claims/${id}`);
}