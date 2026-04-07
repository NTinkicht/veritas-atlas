param(
    [Parameter(Mandatory = $true)]
    [string]$RootDir
)

$ErrorActionPreference = "Stop"

function Ensure-Dir {
    param([Parameter(Mandatory = $true)][string]$Path)
    if ([string]::IsNullOrWhiteSpace($Path)) { throw "Empty path." }
    if (-not (Test-Path $Path)) { New-Item -ItemType Directory -Path $Path -Force | Out-Null }
}

function Write-Utf8NoBom {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Content
    )
    $dir = Split-Path -Parent $Path
    Ensure-Dir -Path $dir
    $enc = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText($Path, $Content, $enc)
    Write-Host "Wrote: $Path"
}

function Commit-Checkpoint {
    param(
        [Parameter(Mandatory = $true)][string]$RootDir,
        [Parameter(Mandatory = $true)][string]$Message
    )

    Push-Location $RootDir
    try {
        git add -A | Out-Null
        git commit -m $Message
        if ($LASTEXITCODE -ne 0) {
            throw "Git commit failed."
        }
        Write-Host "Created git commit: $Message" -ForegroundColor Green
    }
    finally {
        Pop-Location
    }
}

function Build-Frontend {
    param([Parameter(Mandatory = $true)][string]$RootDir)

    $webDir = Join-Path $RootDir "apps\web\veritas-atlas-web"
    Push-Location $webDir
    try {
        npm run build
        if ($LASTEXITCODE -ne 0) {
            throw "Frontend build failed."
        }
    }
    finally {
        Pop-Location
    }
}

Write-Host "Checkpointing current code with git..." -ForegroundColor Cyan
Commit-Checkpoint -RootDir $RootDir -Message ("checkpoint before phase 13A-13B - " + (Get-Date -Format "yyyy-MM-dd HH:mm:ss"))

$webRoot = Join-Path $RootDir "apps\web\veritas-atlas-web\src"
$diagDir = Join-Path $RootDir "_diagnostics\phase-13A-13B"
Ensure-Dir -Path $diagDir

Write-Utf8NoBom -Path (Join-Path $webRoot "api\contracts.ts") -Content @'
export type Guid = string;
export type UtcIsoString = string;

export type ApiError = {
  status: number;
  message: string;
  raw?: unknown;
};

export type PagedResponse<T> = {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
};

export type AuthErrorResponse = {
  code: string;
  message: string;
  timestampUtc: UtcIsoString;
};

export type LoginRequest = {
  username: string;
  password: string;
};

export type LoginResponse = {
  accessToken: string;
  tokenType: string;
  expiresAtUtc: UtcIsoString;
  username: string;
  role: string;
};

export type AuthMeResponse = {
  user: string | null;
  role: string | null;
  isAuthenticated: boolean;
  claims: Array<{
    type: string;
    value: string;
  }>;
};

export type WorkflowTransitionResponse = {
  entityType: string;
  entityId: Guid;
  status: string;
  timestampUtc: UtcIsoString;
  message: string;
};

export type WorkflowValidationFailureResponse = {
  entityType: string;
  entityId: Guid;
  attemptedAction: string;
  reason: string;
  timestampUtc: UtcIsoString;
};

export type WorkflowSeedResponse = {
  caseId: Guid;
  claimAId: Guid;
  claimBId: Guid;
  contradictionId: Guid;
  caseStatus: string;
  contradictionStatus: string;
  timestampUtc: UtcIsoString;
};

export type ScenarioSnapshotResponse = {
  exists: boolean;
  caseId?: Guid | null;
  claimAId?: Guid | null;
  claimBId?: Guid | null;
  contradictionId?: Guid | null;
  createdAtUtc?: UtcIsoString | null;
  caseStatus?: string | null;
  contradictionStatus?: string | null;
  timestampUtc: UtcIsoString;
};

export type ResetResponse = {
  success: boolean;
  message: string;
  timestampUtc: UtcIsoString;
};

export type WorkflowAuditEntry = {
  id: Guid;
  entityType: string;
  entityId: Guid;
  actionName: string;
  previousStatus: string | null;
  nextStatus: string | null;
  role: string;
  succeeded: boolean;
  message: string;
  timestampUtc: UtcIsoString;
};

export type WorkflowAuditResponse = PagedResponse<WorkflowAuditEntry> | WorkflowAuditEntry[];

export type ClaimItem = {
  id: Guid;
  statementId: Guid;
  personId: Guid | null;
  caseId: Guid | null;
  type: string;
  status: string;
  topic: string;
  normalizedText: string;
  isMaterial: boolean;
  createdAtUtc: UtcIsoString;
  updatedAtUtc: UtcIsoString;
};

export type CreateClaimRequest = {
  statementId: Guid;
  topic: string;
  normalizedText: string;
  type: string | null;
  personId: Guid | null;
  caseId: Guid | null;
  isMaterial: boolean;
};

export type GetClaimsResponse = PagedResponse<ClaimItem>;

export type CaseItem = {
  id: Guid;
  status: string;
  title: string;
  summary?: string | null;
  type?: string | null;
  createdAtUtc: UtcIsoString;
  updatedAtUtc: UtcIsoString;
  subjectPersonId?: Guid | null;
  createdBy?: string | null;
};

export type GetCasesResponse = PagedResponse<CaseItem>;

export type ContradictionItem = {
  id: Guid;
  caseId: Guid;
  leftClaimId: Guid;
  rightClaimId: Guid;
  type: string;
  severity: string;
  status: string;
  summary: string;
  rationale?: string | null;
  confidenceScoreId?: Guid | null;
  createdAtUtc: UtcIsoString;
  updatedAtUtc: UtcIsoString;
};

export type GetContradictionsResponse = PagedResponse<ContradictionItem>;

export type StatementItem = {
  id: Guid;
  evidenceId: Guid;
  personId?: Guid | null;
  text: string;
  createdAtUtc: UtcIsoString;
  updatedAtUtc: UtcIsoString;
};
'@

Write-Utf8NoBom -Path (Join-Path $webRoot "api\errors.ts") -Content @'
import type { ApiError } from "./contracts";

export async function toApiError(response: Response): Promise<ApiError> {
  let raw: unknown = null;
  let message = `HTTP ${response.status}`;

  try {
    raw = await response.json();
    if (raw && typeof raw === "object") {
      const candidate =
        (raw as Record<string, unknown>).message ??
        (raw as Record<string, unknown>).reason ??
        (raw as Record<string, unknown>).title ??
        (raw as Record<string, unknown>).detail;

      if (typeof candidate === "string" && candidate.trim().length > 0) {
        message = candidate;
      }
    }
  } catch {
    try {
      const text = await response.text();
      if (text.trim().length > 0) {
        message = text;
      }
    } catch {
      // ignore secondary parse failures
    }
  }

  return {
    status: response.status,
    message,
    raw,
  };
}
'@

Write-Utf8NoBom -Path (Join-Path $webRoot "api\httpAuth.ts") -Content @'
export const ACCESS_TOKEN_KEY = "veritas_atlas_access_token";

export function getAccessToken(): string | null {
  return localStorage.getItem(ACCESS_TOKEN_KEY);
}

export function setAccessToken(token: string): void {
  localStorage.setItem(ACCESS_TOKEN_KEY, token);
}

export function clearAccessToken(): void {
  localStorage.removeItem(ACCESS_TOKEN_KEY);
}

export function getAuthHeaders(): HeadersInit {
  const token = getAccessToken();

  if (!token) {
    return {};
  }

  return {
    Authorization: `Bearer ${token}`,
  };
}
'@

Write-Utf8NoBom -Path (Join-Path $webRoot "api\http.ts") -Content @'
import { toApiError } from "./errors";
import { getAuthHeaders } from "./httpAuth";

function createHeaders(auth: boolean, hasBody: boolean): HeadersInit {
  return {
    ...(hasBody ? { "Content-Type": "application/json" } : {}),
    ...(auth ? getAuthHeaders() : {}),
  };
}

export async function apiGet<T>(url: string, auth = true): Promise<T> {
  const response = await fetch(url, {
    method: "GET",
    headers: createHeaders(auth, false),
  });

  if (!response.ok) {
    throw await toApiError(response);
  }

  return (await response.json()) as T;
}

export async function apiPost<TResponse, TRequest = unknown>(
  url: string,
  body?: TRequest,
  auth = true,
): Promise<TResponse> {
  const response = await fetch(url, {
    method: "POST",
    headers: createHeaders(auth, body !== undefined),
    body: body === undefined ? undefined : JSON.stringify(body),
  });

  if (!response.ok) {
    throw await toApiError(response);
  }

  return (await response.json()) as TResponse;
}
'@

Write-Utf8NoBom -Path (Join-Path $webRoot "api\auth.ts") -Content @'
import type { AuthMeResponse, LoginRequest, LoginResponse } from "./contracts";
import { apiGet, apiPost } from "./http";

export async function login(request: LoginRequest): Promise<LoginResponse> {
  return apiPost<LoginResponse, LoginRequest>("/api/v1/auth/login", request, false);
}

export async function getAuthMe(): Promise<AuthMeResponse> {
  return apiGet<AuthMeResponse>("/api/v1/auth/me", true);
}
'@

Write-Utf8NoBom -Path (Join-Path $webRoot "api\workflowActions.ts") -Content @'
import type { WorkflowSeedResponse, WorkflowTransitionResponse } from "./contracts";
import { apiPost } from "./http";

export async function seedLifecycle(): Promise<WorkflowSeedResponse> {
  return apiPost<WorkflowSeedResponse>("/api/v1/actions/seed/lifecycle");
}

export async function submitCase(caseId: string): Promise<WorkflowTransitionResponse> {
  return apiPost<WorkflowTransitionResponse>(`/api/v1/actions/cases/${caseId}/submit`);
}

export async function approveCase(caseId: string): Promise<WorkflowTransitionResponse> {
  return apiPost<WorkflowTransitionResponse>(`/api/v1/actions/cases/${caseId}/approve`);
}

export async function rejectCase(caseId: string): Promise<WorkflowTransitionResponse> {
  return apiPost<WorkflowTransitionResponse>(`/api/v1/actions/cases/${caseId}/reject`);
}

export async function resolveContradiction(contradictionId: string): Promise<WorkflowTransitionResponse> {
  return apiPost<WorkflowTransitionResponse>(`/api/v1/actions/contradictions/${contradictionId}/resolve`);
}

export async function completeReview(reviewId: string): Promise<WorkflowTransitionResponse> {
  return apiPost<WorkflowTransitionResponse>(`/api/v1/actions/reviews/${reviewId}/complete`);
}
'@

Write-Utf8NoBom -Path (Join-Path $webRoot "api\reviewWorkflow.ts") -Content @'
import type { WorkflowTransitionResponse } from "./contracts";
import { apiPost } from "./http";

export async function sendClaimToReview(claimId: string): Promise<WorkflowTransitionResponse> {
  return apiPost<WorkflowTransitionResponse>(`/api/v1/review-workflow/claims/${claimId}/send-to-review`);
}

export async function returnClaimForEdit(claimId: string): Promise<WorkflowTransitionResponse> {
  return apiPost<WorkflowTransitionResponse>(`/api/v1/review-workflow/claims/${claimId}/return-for-edit`);
}

export async function escalateContradiction(contradictionId: string): Promise<WorkflowTransitionResponse> {
  return apiPost<WorkflowTransitionResponse>(`/api/v1/review-workflow/contradictions/${contradictionId}/escalate`);
}

export async function reopenReview(reviewId: string): Promise<WorkflowTransitionResponse> {
  return apiPost<WorkflowTransitionResponse>(`/api/v1/review-workflow/reviews/${reviewId}/reopen`);
}
'@

Write-Utf8NoBom -Path (Join-Path $webRoot "api\publicationWorkflow.ts") -Content @'
import type { WorkflowTransitionResponse } from "./contracts";
import { apiPost } from "./http";

export async function preparePublication(caseId: string): Promise<WorkflowTransitionResponse> {
  return apiPost<WorkflowTransitionResponse>(`/api/v1/publication-workflow/cases/${caseId}/prepare`);
}

export async function publishCase(caseId: string): Promise<WorkflowTransitionResponse> {
  return apiPost<WorkflowTransitionResponse>(`/api/v1/publication-workflow/cases/${caseId}/publish`);
}

export async function holdCase(caseId: string): Promise<WorkflowTransitionResponse> {
  return apiPost<WorkflowTransitionResponse>(`/api/v1/publication-workflow/cases/${caseId}/hold`);
}
'@

Write-Utf8NoBom -Path (Join-Path $webRoot "api\workflowAudit.ts") -Content @'
import type { PagedResponse, WorkflowAuditEntry, WorkflowAuditResponse } from "./contracts";
import { apiGet } from "./http";

function normalizeAuditResponse(data: WorkflowAuditResponse): PagedResponse<WorkflowAuditEntry> {
  if (Array.isArray(data)) {
    return {
      items: data,
      page: 1,
      pageSize: data.length,
      totalCount: data.length,
      totalPages: 1,
    };
  }

  return data;
}

export async function getWorkflowAuditEntries(): Promise<PagedResponse<WorkflowAuditEntry>> {
  const data = await apiGet<WorkflowAuditResponse>("/api/v1/workflow-audit/entries");
  return normalizeAuditResponse(data);
}
'@

Write-Utf8NoBom -Path (Join-Path $webRoot "api\persistence.ts") -Content @'
import type { ResetResponse, ScenarioSnapshotResponse } from "./contracts";
import { apiGet, apiPost } from "./http";

export async function getScenarioSnapshot(): Promise<ScenarioSnapshotResponse> {
  return apiGet<ScenarioSnapshotResponse>("/api/v1/persistence/snapshot");
}

export async function resetScenarioState(): Promise<ResetResponse> {
  return apiPost<ResetResponse>("/api/v1/persistence/reset");
}
'@

Write-Utf8NoBom -Path (Join-Path $webRoot "api\claims.ts") -Content @'
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
'@

Write-Utf8NoBom -Path (Join-Path $webRoot "api\cases.ts") -Content @'
import type { CaseItem, GetCasesResponse } from "./contracts";
import { apiGet } from "./http";

export async function getCases(page = 1, pageSize = 20): Promise<GetCasesResponse> {
  const params = new URLSearchParams({
    page: String(page),
    pageSize: String(pageSize),
  });

  return apiGet<GetCasesResponse>(`/api/v1/cases?${params.toString()}`);
}

export async function getCaseById(id: string): Promise<CaseItem> {
  return apiGet<CaseItem>(`/api/v1/cases/${id}`);
}
'@

Write-Utf8NoBom -Path (Join-Path $webRoot "api\contradictions.ts") -Content @'
import type { ContradictionItem, GetContradictionsResponse } from "./contracts";
import { apiGet } from "./http";

export async function getContradictions(page = 1, pageSize = 20): Promise<GetContradictionsResponse> {
  const params = new URLSearchParams({
    page: String(page),
    pageSize: String(pageSize),
  });

  return apiGet<GetContradictionsResponse>(`/api/v1/contradictions?${params.toString()}`);
}

export async function getContradictionById(id: string): Promise<ContradictionItem> {
  return apiGet<ContradictionItem>(`/api/v1/contradictions/${id}`);
}
'@

Write-Utf8NoBom -Path (Join-Path $webRoot "api\statements.ts") -Content @'
import type { StatementItem } from "./contracts";
import { apiGet } from "./http";

export async function getStatementById(id: string): Promise<StatementItem> {
  return apiGet<StatementItem>(`/api/v1/statements/${id}`);
}
'@

Write-Utf8NoBom -Path (Join-Path $diagDir "phase-13A-13B-summary.md") -Content @'
# Phase 13A + 13B Summary

Completed:
- locked TypeScript contract catalog in src/api/contracts.ts
- shared API error normalization in src/api/errors.ts
- centralized bearer token handling in src/api/httpAuth.ts
- shared fetch helpers in src/api/http.ts
- API clients for auth, workflow actions, review workflow, publication workflow, persistence, workflow audit, claims, cases, contradictions, statements

Execution notes:
- all IDs are Guid strings
- all timestamps are UTC ISO strings
- all mutation-style endpoints normalize around WorkflowTransitionResponse
- workflow audit client normalizes raw-array or paged responses into a stable paged shape

Next:
- Thread 13C Auth Shell
- Thread 13D Operations Dashboard
'@

Write-Host "Building frontend..." -ForegroundColor Cyan
Build-Frontend -RootDir $RootDir

Write-Host "Phase 13A + 13B bundle DONE" -ForegroundColor Green
