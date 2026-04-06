import { apiGet } from "./client";

export type ClaimItem = {
  id: string;
  statementId: string;
  personId: string | null;
  caseId: string | null;
  type: string;
  status: string;
  topic: string;
  normalizedText: string;
  isMaterial: boolean;
  createdAtUtc: string;
  updatedAtUtc: string;
};

export type ClaimDetail = ClaimItem;

export type ClaimsResponse = {
  items: ClaimItem[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
};

export type CreateClaimRequest = {
  statementId: string;
  topic: string;
  normalizedText: string;
  type?: string;
  personId?: string;
  caseId?: string;
  isMaterial: boolean;
};

export async function getClaims(statementId?: string): Promise<ClaimsResponse> {
  const params = new URLSearchParams({
    page: "1",
    pageSize: "50",
  });

  if (statementId) {
    params.set("statementId", statementId);
  }

  return apiGet<ClaimsResponse>(`/api/v1/claims?${params.toString()}`);
}

export async function getClaimById(id: string): Promise<ClaimDetail> {
  return apiGet<ClaimDetail>(`/api/v1/claims/${id}`);
}

export async function createClaim(request: CreateClaimRequest): Promise<ClaimItem> {
  const response = await fetch("/api/v1/claims", {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
    },
    body: JSON.stringify(request),
  });

  if (!response.ok) {
    const text = await response.text();
    throw new Error(text || `HTTP ${response.status}`);
  }

  return response.json() as Promise<ClaimItem>;
}
