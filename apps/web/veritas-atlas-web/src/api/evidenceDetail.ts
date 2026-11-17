import { apiGet } from "./http";
import { toFreshnessLabel, type FreshnessLabel } from "./evidenceSearch";

// VER-P0-17 (#344): client for the tenant-scoped verified-evidence detail read
// (GET /api/v1/tenant-evidence/detail?ref=, #342). Read-only; no seeded or cached fallback.
// A malformed response is rejected whole and server text is never echoed.

export const DETAIL_PATH = "/api/v1/tenant-evidence/detail";

export type EvidenceChunk = { ordinal: number; elementId: string; text: string };

export type EvidenceDetail = {
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
  chunks: EvidenceChunk[];
  chunksTruncated: boolean;
};

export class MalformedDetailResponse extends Error {
  constructor(detail: string) {
    super(`The evidence service returned an unexpected response (${detail}).`);
    this.name = "MalformedDetailResponse";
  }
}

const REQUIRED_TEXT = ["evidenceRef", "sourceUri", "sourceEntryId", "jurisdiction", "sourceType", "attestationState", "retrievedAtUtc"] as const;

function optionalText(record: Record<string, unknown>, key: string): string | null {
  const value = record[key];
  if (value === null || value === undefined) return null;
  if (typeof value !== "string") throw new MalformedDetailResponse(`${key} is not text`);
  return value;
}

export function buildDetailPath(evidenceRef: string): string {
  return `${DETAIL_PATH}?ref=${encodeURIComponent(evidenceRef)}`;
}

export function parseDetailResponse(raw: unknown, requestedRef?: string): EvidenceDetail {
  if (typeof raw !== "object" || raw === null) throw new MalformedDetailResponse("not an object");
  const body = raw as Record<string, unknown>;
  for (const key of REQUIRED_TEXT) {
    if (typeof body[key] !== "string") throw new MalformedDetailResponse(key);
  }
  if (typeof body.freshnessEligible !== "boolean") throw new MalformedDetailResponse("freshnessEligible");
  if (typeof body.chunksTruncated !== "boolean") throw new MalformedDetailResponse("chunksTruncated");
  // Never show evidence other than what was asked for.
  if (requestedRef !== undefined && body.evidenceRef !== requestedRef) throw new MalformedDetailResponse("evidenceRef mismatch");
  if (!Array.isArray(body.chunks)) throw new MalformedDetailResponse("chunks");
  const chunks = body.chunks.map((item, index): EvidenceChunk => {
    if (typeof item !== "object" || item === null) throw new MalformedDetailResponse(`chunk ${index}`);
    const c = item as Record<string, unknown>;
    if (typeof c.ordinal !== "number" || !Number.isInteger(c.ordinal)) throw new MalformedDetailResponse(`chunk ${index} ordinal`);
    if (typeof c.elementId !== "string") throw new MalformedDetailResponse(`chunk ${index} elementId`);
    if (typeof c.text !== "string") throw new MalformedDetailResponse(`chunk ${index} text`);
    return { ordinal: c.ordinal, elementId: c.elementId, text: c.text };
  });
  return {
    evidenceRef: body.evidenceRef as string,
    sourceUri: body.sourceUri as string,
    sourceEntryId: body.sourceEntryId as string,
    jurisdiction: body.jurisdiction as string,
    sourceType: body.sourceType as string,
    attestationState: body.attestationState as string,
    retrievedAtUtc: body.retrievedAtUtc as string,
    freshness: toFreshnessLabel(body.freshness),
    freshnessEligible: body.freshnessEligible,
    freshnessReason: optionalText(body, "freshnessReason"),
    freshnessEvaluatedAt: optionalText(body, "freshnessEvaluatedAt"),
    parserName: optionalText(body, "parserName"),
    parserVersion: optionalText(body, "parserVersion"),
    chunks,
    chunksTruncated: body.chunksTruncated,
  };
}

export type DetailFailureKind = "unauthenticated" | "forbidden" | "notfound" | "invalid" | "offline" | "server" | "malformed";
export type DetailFailure = { kind: DetailFailureKind; message: string };

// Fixed messages only. Unknown, unverified and other-organization references are indistinguishable (404).
export function classifyDetailFailure(error: unknown, online: boolean = true): DetailFailure {
  if (error instanceof MalformedDetailResponse) return { kind: "malformed", message: error.message };
  const status = typeof error === "object" && error !== null ? (error as { status?: unknown }).status : undefined;
  if (status === 401) return { kind: "unauthenticated", message: "Your session has ended or is not valid. Sign in again to view evidence." };
  if (status === 403) return { kind: "forbidden", message: "Your account is not permitted to read evidence for an organization." };
  if (status === 404) return { kind: "notfound", message: "No verified evidence with that reference is available to your organization." };
  if (status === 400) return { kind: "invalid", message: "That evidence reference is not valid." };
  if (typeof status === "number") {
    return { kind: "server", message: `The evidence service failed (HTTP ${status}). Nothing is shown rather than a guess.` };
  }
  return {
    kind: "offline",
    message: online ? "The evidence service could not be reached. Check your connection and try again." : "You appear to be offline. Reconnect and try again.",
  };
}

export async function getEvidenceDetail(evidenceRef: string): Promise<EvidenceDetail> {
  return parseDetailResponse(await apiGet<unknown>(buildDetailPath(evidenceRef)), evidenceRef);
}
