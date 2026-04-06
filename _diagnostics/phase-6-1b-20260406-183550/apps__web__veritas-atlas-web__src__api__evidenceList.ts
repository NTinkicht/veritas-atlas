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

export async function getEvidenceList(): Promise<EvidenceResponse> {
  return apiGet<EvidenceResponse>("/api/v1/evidence?page=1&pageSize=50");
}
