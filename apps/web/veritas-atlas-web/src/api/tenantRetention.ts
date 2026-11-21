import { apiGet } from "./http";

// VER-P0-18 / VER-P0-17 (#366): client for the admin-only retention reads of one raw retrieval
// (GET /api/v1/tenant-raw-retrievals/{id}/retention-eligibility and /retention). Read-only: the
// eligibility is a dry run and nothing is ever deleted, held or re-windowed from here. No seeded
// fallback; a malformed response is rejected whole and server text is never echoed.

export const RETENTION_DENIAL_LABELS: Readonly<Record<string, string>> = {
  legal_hold: "A legal hold is recorded.",
  live_reference: "A parse run still references these bytes.",
  audit_trail_missing: "No retrieval attestation exists, so there is no audit trail.",
  retention_not_elapsed: "No retain-until window has been recorded or it has not elapsed.",
};

export type RetentionEligibility = {
  outcome: "Allow" | "Deny";
  denialCode: string | null;
  parseRunCount: number;
  attestationCount: number;
  recordVersion: number;
  legalHold: boolean;
  retainUntilUtc: string | null;
};

export type RetentionVersion = {
  version: number;
  legalHold: boolean;
  retainUntilUtc: string | null;
  actorSubject: string;
  createdAtUtc: string;
};

export type RetentionView = { eligibility: RetentionEligibility; history: RetentionVersion[] };

export class MalformedRetentionResponse extends Error {
  constructor(detail: string) {
    super(`The retention view returned an unexpected response (${detail}).`);
    this.name = "MalformedRetentionResponse";
  }
}

const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

function record(raw: unknown, what: string): Record<string, unknown> {
  if (typeof raw !== "object" || raw === null || Array.isArray(raw)) throw new MalformedRetentionResponse(what);
  return raw as Record<string, unknown>;
}
function count(r: Record<string, unknown>, key: string): number {
  const v = r[key];
  if (typeof v !== "number" || !Number.isInteger(v) || v < 0) throw new MalformedRetentionResponse(key);
  return v;
}
function bool(r: Record<string, unknown>, key: string): boolean {
  if (typeof r[key] !== "boolean") throw new MalformedRetentionResponse(key);
  return r[key] as boolean;
}
function instant(r: Record<string, unknown>, key: string, nullable: boolean): string | null {
  const v = r[key];
  if (v === null || v === undefined) {
    if (nullable) return null;
    throw new MalformedRetentionResponse(key);
  }
  if (typeof v !== "string" || Number.isNaN(Date.parse(v))) throw new MalformedRetentionResponse(key);
  return v;
}

export function parseRetentionEligibility(raw: unknown): RetentionEligibility {
  const r = record(raw, "eligibility");
  const outcome = r.outcome;
  if (outcome !== "Allow" && outcome !== "Deny") throw new MalformedRetentionResponse("outcome");
  const code = r.denialCode ?? null;
  if (outcome === "Allow" ? code !== null : typeof code !== "string" || !Object.hasOwn(RETENTION_DENIAL_LABELS, code)) {
    throw new MalformedRetentionResponse("denialCode");
  }
  return {
    outcome,
    denialCode: code as string | null,
    parseRunCount: count(r, "parseRunCount"),
    attestationCount: count(r, "attestationCount"),
    recordVersion: count(r, "recordVersion"),
    legalHold: bool(r, "legalHold"),
    retainUntilUtc: instant(r, "retainUntilUtc", true),
  };
}

export function parseRetentionHistory(raw: unknown): RetentionVersion[] {
  const versions = record(raw, "history").versions;
  if (!Array.isArray(versions)) throw new MalformedRetentionResponse("versions");
  return versions.map((item: unknown, i): RetentionVersion => {
    const r = record(item, `version ${i}`);
    const subject = r.actorSubject;
    if (typeof subject !== "string" || subject.trim() === "") throw new MalformedRetentionResponse(`version ${i} actorSubject`);
    return {
      version: count(r, "version"),
      legalHold: bool(r, "legalHold"),
      retainUntilUtc: instant(r, "retainUntilUtc", true),
      actorSubject: subject,
      createdAtUtc: instant(r, "createdAtUtc", false) as string,
    };
  });
}

export type RetentionFailureKind = "unauthenticated" | "forbidden" | "not-found" | "offline" | "server" | "malformed";
export type RetentionFailure = { kind: RetentionFailureKind; message: string };

// Fixed messages only: server text is never echoed.
export function classifyRetentionFailure(error: unknown, online: boolean = true): RetentionFailure {
  if (error instanceof MalformedRetentionResponse) return { kind: "malformed", message: error.message };
  const status = typeof error === "object" && error !== null ? (error as { status?: unknown }).status : undefined;
  if (status === 401) return { kind: "unauthenticated", message: "Your session has ended or is not valid. Sign in again." };
  if (status === 403) return { kind: "forbidden", message: "Only an organization administrator may read retention." };
  if (status === 404) return { kind: "not-found", message: "That raw retrieval was not found." };
  if (typeof status === "number") {
    return { kind: "server", message: `The retention view failed (HTTP ${status}). Nothing is shown rather than a guess.` };
  }
  return {
    kind: "offline",
    message: online ? "The retention view could not be reached. Check your connection and try again." : "You appear to be offline. Reconnect and try again.",
  };
}

export function retentionPath(retrievalId: string, leaf: "retention" | "retention-eligibility"): string {
  if (!GUID.test(retrievalId)) throw new MalformedRetentionResponse("retrieval id");
  return `/api/v1/tenant-raw-retrievals/${retrievalId}/${leaf}`;
}

export async function getRetentionView(retrievalId: string): Promise<RetentionView> {
  const [eligibility, history] = await Promise.all([
    apiGet<unknown>(retentionPath(retrievalId, "retention-eligibility")),
    apiGet<unknown>(retentionPath(retrievalId, "retention")),
  ]);
  return { eligibility: parseRetentionEligibility(eligibility), history: parseRetentionHistory(history) };
}
