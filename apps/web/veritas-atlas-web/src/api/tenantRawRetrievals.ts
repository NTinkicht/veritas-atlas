import { apiGet } from "./http";

// VER-P0-17 (#354): client for the admin-only raw retrieval metadata list (GET /api/v1/tenant-raw-retrievals,
// VER-P0-06). Read-only: it never parses, verifies or edits. A raw retrieval is unparsed bytes metadata and
// carries no evidence reference. No seeded fallback; a malformed response is rejected whole and server text
// is never echoed.

export const TENANT_RAW_RETRIEVALS_PATH = "/api/v1/tenant-raw-retrievals";

export type RawRetrieval = {
  id: string;
  finalUri: string;
  contentSha256: string;
  byteLength: number;
  retrievedAtUtc: string;
};

export class MalformedRawRetrievalsResponse extends Error {
  constructor(detail: string) {
    super(`The raw retrieval list returned an unexpected response (${detail}).`);
    this.name = "MalformedRawRetrievalsResponse";
  }
}

const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const SHA256 = /^[0-9a-f]{64}$/i;

export function parseRawRetrievals(raw: unknown): RawRetrieval[] {
  if (!Array.isArray(raw)) throw new MalformedRawRetrievalsResponse("not a list");
  const seen = new Set<string>();
  return raw.map((item: unknown, i): RawRetrieval => {
    if (typeof item !== "object" || item === null) throw new MalformedRawRetrievalsResponse(`item ${i}`);
    const r = item as Record<string, unknown>;
    if (typeof r.id !== "string" || !GUID.test(r.id)) throw new MalformedRawRetrievalsResponse(`item ${i} id`);
    if (typeof r.finalUri !== "string" || r.finalUri.trim() === "") throw new MalformedRawRetrievalsResponse(`item ${i} finalUri`);
    if (typeof r.contentSha256 !== "string" || !SHA256.test(r.contentSha256)) throw new MalformedRawRetrievalsResponse(`item ${i} contentSha256`);
    if (typeof r.byteLength !== "number" || !Number.isInteger(r.byteLength) || r.byteLength < 0) throw new MalformedRawRetrievalsResponse(`item ${i} byteLength`);
    if (typeof r.retrievedAtUtc !== "string" || Number.isNaN(Date.parse(r.retrievedAtUtc))) throw new MalformedRawRetrievalsResponse(`item ${i} retrievedAtUtc`);
    const id = r.id.toLowerCase();
    if (seen.has(id)) throw new MalformedRawRetrievalsResponse(`item ${i} duplicate id`);
    seen.add(id);
    return { id: r.id, finalUri: r.finalUri, contentSha256: r.contentSha256, byteLength: r.byteLength, retrievedAtUtc: r.retrievedAtUtc };
  });
}

export type RawRetrievalsFailureKind = "unauthenticated" | "forbidden" | "offline" | "server" | "malformed";
export type RawRetrievalsFailure = { kind: RawRetrievalsFailureKind; message: string };

// Fixed messages only: server text is never echoed.
export function classifyRawRetrievalsFailure(error: unknown, online: boolean = true): RawRetrievalsFailure {
  if (error instanceof MalformedRawRetrievalsResponse) return { kind: "malformed", message: error.message };
  const status = typeof error === "object" && error !== null ? (error as { status?: unknown }).status : undefined;
  if (status === 401) return { kind: "unauthenticated", message: "Your session has ended or is not valid. Sign in again." };
  if (status === 403) return { kind: "forbidden", message: "Only an organization administrator may read raw retrievals." };
  if (typeof status === "number") {
    return { kind: "server", message: `The raw retrieval list failed (HTTP ${status}). Nothing is shown rather than a guess.` };
  }
  return {
    kind: "offline",
    message: online ? "The raw retrieval list could not be reached. Check your connection and try again." : "You appear to be offline. Reconnect and try again.",
  };
}

export async function listRawRetrievals(sourceId: string): Promise<RawRetrieval[]> {
  return parseRawRetrievals(await apiGet<unknown>(`${TENANT_RAW_RETRIEVALS_PATH}?sourceId=${encodeURIComponent(sourceId)}`));
}
