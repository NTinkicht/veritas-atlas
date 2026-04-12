import { getCases } from "./cases";
import { getClaims } from "./claims";
import { getContradictions } from "./contradictions";
import { getWorkflowAuditEntries } from "./workflowAudit";
import { getScenarioSnapshot } from "./persistence";
import type {
  CaseItem,
  ClaimItem,
  ContradictionItem,
  PagedResponse,
  WorkflowAuditEntry,
  ScenarioSnapshotResponse,
} from "./contracts";

export type CasesResponse = PagedResponse<CaseItem>;
export type ClaimsResponse = PagedResponse<ClaimItem>;
export type ContradictionsResponse = PagedResponse<ContradictionItem>;

export type DashboardData = {
  cases: CasesResponse;
  claims: ClaimsResponse;
  contradictions: ContradictionsResponse;
  audit: WorkflowAuditEntry[];
  snapshot: ScenarioSnapshotResponse | null;
};

export async function getDashboardCases(page = 1, pageSize = 5): Promise<CasesResponse> {
  return getCases(page, pageSize);
}

export async function getDashboardClaims(page = 1, pageSize = 5): Promise<ClaimsResponse> {
  return getClaims(page, pageSize);
}

export async function getDashboardContradictions(page = 1, pageSize = 5): Promise<ContradictionsResponse> {
  return getContradictions(page, pageSize);
}

function normalizeAuditItems(value: unknown): WorkflowAuditEntry[] {
  if (Array.isArray(value)) {
    return value as WorkflowAuditEntry[];
  }

  if (value && typeof value === "object") {
    const record = value as Record<string, unknown>;
    if (Array.isArray(record.items)) {
      return record.items as WorkflowAuditEntry[];
    }
  }

  return [];
}

export async function getDashboardData(): Promise<DashboardData> {
  const [cases, claims, contradictions, auditResult, snapshot] = await Promise.all([
    getDashboardCases(1, 5),
    getDashboardClaims(1, 5),
    getDashboardContradictions(1, 5),
    getWorkflowAuditEntries(),
    getScenarioSnapshot().catch(() => null),
  ]);

  return {
    cases,
    claims,
    contradictions,
    audit: normalizeAuditItems(auditResult),
    snapshot,
  };
}