import { getHealth, getDatabaseHealth } from "./health";
import { getCases, type CasesResponse } from "./cases";
import { getReviews, type ReviewsResponse } from "./reviews";
import { apiGet } from "./client";

export type AgentRunsListItem = {
  id: string;
  caseId: string | null;
  agentName: string;
  agentType: string;
  status: string;
  startedAtUtc: string;
  completedAtUtc: string | null;
  createdAtUtc: string;
};

export type AgentRunsResponse = {
  items: AgentRunsListItem[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
};

export type DashboardData = {
  health: unknown;
  dbHealth: unknown;
  cases: CasesResponse;
  reviews: ReviewsResponse;
  agentRuns: AgentRunsResponse;
};

export async function getAgentRuns(): Promise<AgentRunsResponse> {
  return apiGet<AgentRunsResponse>("/api/v1/agent-runs?page=1&pageSize=20");
}

export async function getDashboardData(): Promise<DashboardData> {
  const [health, dbHealth, cases, reviews, agentRuns] = await Promise.all([
    getHealth(),
    getDatabaseHealth(),
    getCases(),
    getReviews(),
    getAgentRuns(),
  ]);

  return {
    health,
    dbHealth,
    cases,
    reviews,
    agentRuns,
  };
}
