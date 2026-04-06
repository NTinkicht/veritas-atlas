import { apiGet } from "./client";

export type EvidenceItem = {
  id: string;
  documentId: string | null;
  status: string;
  createdAtUtc: string;
  updatedAtUtc: string;
};

export type EvidenceResponse = {
  items: EvidenceItem[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
};

export type EvidenceQuery = {
  documentId?: string;
  status?: string;
};

export async function getEvidenceList(query?: EvidenceQuery): Promise<EvidenceResponse> {
  const params = new URLSearchParams({
    page: "1",
    pageSize: "50",
  });

  if (query?.documentId) params.set("documentId", query.documentId);
  if (query?.status && query.status !== "All") params.set("status", query.status);

  return apiGet<EvidenceResponse>(`/api/v1/evidence?${params.toString()}`);
}
