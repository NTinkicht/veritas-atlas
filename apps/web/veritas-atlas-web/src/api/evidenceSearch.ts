import { apiGet } from "./http";

// VER-P0-17 (#161): client for the tenant-scoped verified-evidence search API (VER-P0-09, #159).
// Everything shown comes from this one endpoint; there is no seeded or cached fallback.

export type FreshnessLabel = "CURRENT" | "STALE" | "SUPERSEDED" | "UNKNOWN";

export type EvidenceHit = {
  evidenceRef: string;
  sourceUri: string;
  sourceEntryId: string;
  jurisdiction: string;
  sourceType: string;
  attestationState: string;
  retrievedAtUtc: string;
  freshness: FreshnessLabel;
  freshnessEligible: boolean;
  freshnessReason: string | null;
  freshnessEvaluatedAt: string | null;
  parserName: string | null;
  parserVersion: string | null;
};

export type EvidencePage = { hits: EvidenceHit[]; nextCursor: string | null };

export type SearchParams = {
  q: string;
  freshness?: FreshnessLabel | "";
  jurisdiction?: string;
  type?: string;
  limit?: number;
  cursor?: string | null;
};

export const SEARCH_PATH = "/api/v1/tenant-evidence/search";
export const MAX_QUERY_LENGTH = 256;

// The API serialises the FreshnessState enum as a number (Current=0, Stale=1, Unknown=2,
// Superseded=3); a string name is accepted too. Anything unrecognised is UNKNOWN, never CURRENT.
const BY_NUMBER: Record<number, FreshnessLabel> = { 0: "CURRENT", 1: "STALE", 2: "UNKNOWN", 3: "SUPERSEDED" };
const BY_NAME: Record<string, FreshnessLabel> = {
  current: "CURRENT",
  stale: "STALE",
  unknown: "UNKNOWN",
  superseded: "SUPERSEDED",
};

export function toFreshnessLabel(value: unknown): FreshnessLabel {
  if (typeof value === "number") return BY_NUMBER[value] ?? "UNKNOWN";
  if (typeof value === "string") return BY_NAME[value.trim().toLowerCase()] ?? "UNKNOWN";
  return "UNKNOWN";
}

export function buildSearchPath(params: SearchParams): string {
  const query = new URLSearchParams();
  query.set("q", params.q.trim());
  if (params.freshness) query.set("freshness", params.freshness.toLowerCase());
  if (params.jurisdiction?.trim()) query.set("jurisdiction", params.jurisdiction.trim());
  if (params.type?.trim()) query.set("type", params.type.trim());
  if (params.limit) query.set("limit", String(params.limit));
  if (params.cursor) query.set("cursor", params.cursor);
  return `${SEARCH_PATH}?${query.toString()}`;
}

export class MalformedSearchResponse extends Error {
  constructor(detail: string) {
    super(`The search service returned an unexpected response (${detail}).`);
    this.name = "MalformedSearchResponse";
  }
}

const REQUIRED_TEXT = [
  "evidenceRef",
  "sourceUri",
  "sourceEntryId",
  "jurisdiction",
  "sourceType",
  "attestationState",
  "retrievedAtUtc",
] as const;

function optionalText(record: Record<string, unknown>, key: string): string | null {
  const value = record[key];
  if (value === null || value === undefined) return null;
  if (typeof value !== "string") throw new MalformedSearchResponse(`${key} is not text`);
  return value;
}

// Fail closed: one malformed hit rejects the whole page rather than showing a partial,
// possibly mislabelled, list.
export function parseSearchResponse(raw: unknown): EvidencePage {
  if (typeof raw !== "object" || raw === null) throw new MalformedSearchResponse("not an object");
  const body = raw as Record<string, unknown>;
  if (!Array.isArray(body.results)) throw new MalformedSearchResponse("results missing");
  const hits = body.results.map((item, index): EvidenceHit => {
    if (typeof item !== "object" || item === null) throw new MalformedSearchResponse(`hit ${index}`);
    const record = item as Record<string, unknown>;
    for (const key of REQUIRED_TEXT) {
      if (typeof record[key] !== "string") throw new MalformedSearchResponse(`hit ${index} ${key}`);
    }
    if (typeof record.freshnessEligible !== "boolean") {
      throw new MalformedSearchResponse(`hit ${index} freshnessEligible`);
    }
    return {
      evidenceRef: record.evidenceRef as string,
      sourceUri: record.sourceUri as string,
      sourceEntryId: record.sourceEntryId as string,
      jurisdiction: record.jurisdiction as string,
      sourceType: record.sourceType as string,
      attestationState: record.attestationState as string,
      retrievedAtUtc: record.retrievedAtUtc as string,
      freshness: toFreshnessLabel(record.freshness),
      freshnessEligible: record.freshnessEligible,
      freshnessReason: optionalText(record, "freshnessReason"),
      freshnessEvaluatedAt: optionalText(record, "freshnessEvaluatedAt"),
      parserName: optionalText(record, "parserName"),
      parserVersion: optionalText(record, "parserVersion"),
    };
  });
  const next = body.nextCursor;
  if (next !== null && next !== undefined && typeof next !== "string") {
    throw new MalformedSearchResponse("nextCursor is not text");
  }
  return { hits, nextCursor: typeof next === "string" && next.length > 0 ? next : null };
}

export type FailureKind = "unauthenticated" | "forbidden" | "invalid" | "offline" | "server" | "malformed";

export type SearchFailure = { kind: FailureKind; message: string };

export function classifyFailure(error: unknown, online: boolean = true): SearchFailure {
  if (error instanceof MalformedSearchResponse) return { kind: "malformed", message: error.message };
  const status = typeof error === "object" && error !== null ? (error as { status?: unknown }).status : undefined;
  if (status === 401) {
    return { kind: "unauthenticated", message: "Your session has ended or is not valid. Sign in again to search evidence." };
  }
  if (status === 403) {
    return { kind: "forbidden", message: "Your account is not permitted to search evidence for an organization." };
  }
  if (status === 400) {
    return { kind: "invalid", message: "The search was rejected: check the query and filters and try again." };
  }
  if (typeof status === "number") {
    return { kind: "server", message: `The search service failed (HTTP ${status}). Nothing is shown rather than a guess.` };
  }
  if (!online) {
    return { kind: "offline", message: "You appear to be offline. Reconnect and search again." };
  }
  return { kind: "offline", message: "The search service could not be reached. Check your connection and try again." };
}

export async function searchEvidence(params: SearchParams): Promise<EvidencePage> {
  return parseSearchResponse(await apiGet<unknown>(buildSearchPath(params)));
}

// Only a CURRENT hit the server also marked eligible is styled as current.
export function isPresentedAsCurrent(hit: Pick<EvidenceHit, "freshness" | "freshnessEligible">): boolean {
  return hit.freshness === "CURRENT" && hit.freshnessEligible;
}

export function formatInstant(value: string | null): string {
  if (!value) return "not recorded";
  const time = Date.parse(value);
  return Number.isNaN(time) ? value : new Date(time).toISOString().replace(".000Z", "Z");
}

