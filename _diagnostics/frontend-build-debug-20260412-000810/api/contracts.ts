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

export type LegacyCompatibilityDoNotUseDirectly = {
  primaryClaimId?: Guid;
  secondaryClaimId?: Guid;
  contradictionType?: string;
  topic?: string;
  polarity?: string;
  status?: string;
  predicate?: string;
  object?: string;
  createdAt?: UtcIsoString;
};