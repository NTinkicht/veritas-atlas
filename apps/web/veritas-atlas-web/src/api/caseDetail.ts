import { apiGet } from "./client";

export type CaseDetailClaim = {
  id: string;
  statementId: string;
  personId: string | null;
  type: string;
  status: string;
  topic: string | null;
  normalizedText: string | null;
  isMaterial: boolean;
  createdAtUtc: string;
  updatedAtUtc: string;
};

export type CaseDetailContradiction = {
  id: string;
  caseId: string;
  leftClaimId: string;
  rightClaimId: string;
  type: string;
  severity: string;
  status: string;
  summary: string;
  rationale: string | null;
  createdAtUtc: string;
  updatedAtUtc: string;
};

export type CaseDetailReview = {
  id: string;
  caseId: string;
  type: string;
  status: string;
  decision: string;
  reviewer: string;
  notes: string | null;
  createdAtUtc: string;
  updatedAtUtc: string;
  reviewedAtUtc: string | null;
};

export type CaseDetailPublication = {
  id: string;
  caseId: string;
  channel: string;
  status: string;
  title: string;
  slug: string;
  createdAtUtc: string;
  updatedAtUtc: string;
  publishedAtUtc: string | null;
};

export type CaseDetailResponse = {
  id: string;
  title: string;
  summary: string | null;
  status: string;
  subjectPersonId: string | null;
  createdAtUtc: string;
  updatedAtUtc: string;
  claims: CaseDetailClaim[];
  contradictions: CaseDetailContradiction[];
  reviews: CaseDetailReview[];
  publications: CaseDetailPublication[];
};

export async function getCaseById(id: string): Promise<CaseDetailResponse> {
  return apiGet<CaseDetailResponse>(`/api/v1/cases/${id}`);
}
