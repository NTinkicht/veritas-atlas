import { apiGet } from "./http";

// VER-P0-17: client for the tenant source registry list (GET /api/v1/tenant-sources, VER-P0-03/04).
// Read-only: the server alone decides which sources the organization has registered; this client never
// registers, edits or fetches. No seeded fallback; a malformed response is rejected whole.

export const TENANT_SOURCES_PATH = "/api/v1/tenant-sources";

export type TenantSource = { id: string; canonicalKey: string; createdAtUtc: string };

export class MalformedTenantSourcesResponse extends Error {
  constructor(detail: string) {
    super(`The source registry returned an unexpected response (${detail}).`);
    this.name = "MalformedTenantSourcesResponse";
  }
}

const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

export function parseTenantSources(raw: unknown): TenantSource[] {
  if (typeof raw !== "object" || raw === null) throw new MalformedTenantSourcesResponse("not an object");
  const items = (raw as Record<string, unknown>).items;
  if (!Array.isArray(items)) throw new MalformedTenantSourcesResponse("items");
  const seen = new Set<string>();
  return items.map((item: unknown, i): TenantSource => {
    if (typeof item !== "object" || item === null) throw new MalformedTenantSourcesResponse(`item ${i}`);
    const s = item as Record<string, unknown>;
    if (typeof s.id !== "string" || !GUID.test(s.id)) throw new MalformedTenantSourcesResponse(`item ${i} id`);
    if (typeof s.canonicalKey !== "string" || s.canonicalKey.trim() === "") throw new MalformedTenantSourcesResponse(`item ${i} canonicalKey`);
    if (typeof s.createdAtUtc !== "string" || Number.isNaN(Date.parse(s.createdAtUtc))) {
      throw new MalformedTenantSourcesResponse(`item ${i} createdAtUtc`);
    }
    const id = s.id.toLowerCase();
    // The registry never repeats an id; a repeat means the list cannot be trusted.
    if (seen.has(id)) throw new MalformedTenantSourcesResponse(`item ${i} duplicate id`);
    seen.add(id);
    return { id: s.id, canonicalKey: s.canonicalKey, createdAtUtc: s.createdAtUtc };
  });
}

export type TenantSourcesFailureKind = "unauthenticated" | "forbidden" | "offline" | "server" | "malformed";
export type TenantSourcesFailure = { kind: TenantSourcesFailureKind; message: string };

// Fixed messages only: server text is never echoed.
export function classifyTenantSourcesFailure(error: unknown, online: boolean = true): TenantSourcesFailure {
  if (error instanceof MalformedTenantSourcesResponse) return { kind: "malformed", message: error.message };
  const status = typeof error === "object" && error !== null ? (error as { status?: unknown }).status : undefined;
  if (status === 401) return { kind: "unauthenticated", message: "Your session has ended or is not valid. Sign in again." };
  if (status === 403) return { kind: "forbidden", message: "Your account needs an organization to list its sources." };
  if (typeof status === "number") {
    return { kind: "server", message: `The source registry failed (HTTP ${status}). Nothing is shown rather than a guess.` };
  }
  return {
    kind: "offline",
    message: online ? "The source registry could not be reached. Check your connection and try again." : "You appear to be offline. Reconnect and try again.",
  };
}

export async function listTenantSources(): Promise<TenantSource[]> {
  return parseTenantSources(await apiGet<unknown>(TENANT_SOURCES_PATH));
}
