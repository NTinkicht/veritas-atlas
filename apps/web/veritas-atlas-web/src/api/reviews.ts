import { apiGet } from "./client";

export type ReviewsListItem = {
  id: string;
  caseId: string;
  type: string;
  status: string;
  decision: string;
  reviewer: string;
  createdAtUtc: string;
  reviewedAtUtc: string | null;
};

export type ReviewsResponse = {
  items: ReviewsListItem[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
};

export async function getReviews(): Promise<ReviewsResponse> {
  return apiGet<ReviewsResponse>("/api/v1/reviews?page=1&pageSize=20");
}
