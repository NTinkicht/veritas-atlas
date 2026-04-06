import { apiGet } from "./client";

export type ContradictionItem = {
  id: string;
  primaryClaimId: string;
  secondaryClaimId: string;
  caseId: string | null;
  topic: string;
  summary: string;
  contradictionType: string;
  severity: string;
  status: string;
  createdAtUtc: string;
  updatedAtUtc: string;
};

export type ContradictionDetail = ContradictionItem;

export type ContradictionsResponse = {
  items: ContradictionItem[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
};

export type CreateContradictionRequest = {
  primaryClaimId: string;
  secondaryClaimId: string;
  topic: string;
  summary: string;
  contradictionType?: string;
  severity?: string;
  caseId?: string;
};

export async function getContradictions(claimId?: string, caseId?: string): Promise<ContradictionsResponse> {
  const params = new URLSearchParams({
    page: "1",
    pageSize: "50",
  });

  if (claimId) {
    params.set("claimId", claimId);
  }

  if (caseId) {
    params.set("caseId", caseId);
  }

  return apiGet<ContradictionsResponse>(`/api/v1/contradictions?${params.toString()}`);
}

export async function getContradictionById(id: string): Promise<ContradictionDetail> {
  return apiGet<ContradictionDetail>(`/api/v1/contradictions/${id}`);
}

export async function createContradiction(request: CreateContradictionRequest): Promise<ContradictionItem> {
  const response = await fetch("/api/v1/contradictions", {
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

  return response.json() as Promise<ContradictionItem>;
}