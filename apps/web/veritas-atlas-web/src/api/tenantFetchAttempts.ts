import { apiGet } from "./http";

// VER-P0-17 (#340): client for the admin-only fetch-attempt ledger read (GET /api/v1/tenant-fetch-attempts,
// VER-P0-06). Read-only: it never retries, fetches or edits. No seeded fallback; a malformed response is
// rejected whole and server text is never echoed.

export const TENANT_FETCH_ATTEMPTS_PATH = "/api/v1/tenant-fetch-attempts";

export type FetchAttempt = {
  id: string;
  requestedUri: string;
  attemptedAtUtc: string;
  outcome: "succeeded" | "failed";
  failureClass: "network" | "timeout" | "http_status" | "size_limit" | "policy_denied" | null;
  httpStatus: number | null;
};

export class MalformedFetchAttemptsResponse extends Error {
  constructor(detail: string) {
    super(`The fetch attempt ledger returned an unexpected response (${detail}).`);
    this.name = "MalformedFetchAttemptsResponse";
  }
}

const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const FAILURES = new Set(["network", "timeout", "http_status", "size_limit", "policy_denied"]);

export function parseFetchAttempts(raw: unknown): FetchAttempt[] {
  if (!Array.isArray(raw)) throw new MalformedFetchAttemptsResponse("not a list");
  const seen = new Set<string>();
  return raw.map((item: unknown, i): FetchAttempt => {
    if (typeof item !== "object" || item === null) throw new MalformedFetchAttemptsResponse(`item ${i}`);
    const a = item as Record<string, unknown>;
    if (typeof a.id !== "string" || !GUID.test(a.id)) throw new MalformedFetchAttemptsResponse(`item ${i} id`);
    if (typeof a.requestedUri !== "string" || a.requestedUri.trim() === "") throw new MalformedFetchAttemptsResponse(`item ${i} requestedUri`);
    if (typeof a.attemptedAtUtc !== "string" || Number.isNaN(Date.parse(a.attemptedAtUtc))) {
      throw new MalformedFetchAttemptsResponse(`item ${i} attemptedAtUtc`);
    }
    if (a.outcome !== "succeeded" && a.outcome !== "failed") throw new MalformedFetchAttemptsResponse(`item ${i} outcome`);
    const failureClass = a.failureClass ?? null;
    if (failureClass !== null && (typeof failureClass !== "string" || !FAILURES.has(failureClass))) {
      throw new MalformedFetchAttemptsResponse(`item ${i} failureClass`);
    }
    // A success never carries a failure class; a failure always does.
    if ((a.outcome === "succeeded") !== (failureClass === null)) throw new MalformedFetchAttemptsResponse(`item ${i} outcome/failureClass`);
    const httpStatus = a.httpStatus ?? null;
    if (httpStatus !== null && (typeof httpStatus !== "number" || !Number.isInteger(httpStatus) || httpStatus < 100 || httpStatus > 599)) {
      throw new MalformedFetchAttemptsResponse(`item ${i} httpStatus`);
    }
    const id = a.id.toLowerCase();
    if (seen.has(id)) throw new MalformedFetchAttemptsResponse(`item ${i} duplicate id`);
    seen.add(id);
    return {
      id: a.id,
      requestedUri: a.requestedUri,
      attemptedAtUtc: a.attemptedAtUtc,
      outcome: a.outcome,
      failureClass: failureClass as FetchAttempt["failureClass"],
      httpStatus: httpStatus as number | null,
    };
  });
}

export type FetchAttemptsFailureKind = "unauthenticated" | "forbidden" | "offline" | "server" | "malformed";
export type FetchAttemptsFailure = { kind: FetchAttemptsFailureKind; message: string };

// Fixed messages only: server text is never echoed.
export function classifyFetchAttemptsFailure(error: unknown, online: boolean = true): FetchAttemptsFailure {
  if (error instanceof MalformedFetchAttemptsResponse) return { kind: "malformed", message: error.message };
  const status = typeof error === "object" && error !== null ? (error as { status?: unknown }).status : undefined;
  if (status === 401) return { kind: "unauthenticated", message: "Your session has ended or is not valid. Sign in again." };
  if (status === 403) return { kind: "forbidden", message: "Only an organization administrator may read fetch attempts." };
  if (typeof status === "number") {
    return { kind: "server", message: `The fetch attempt ledger failed (HTTP ${status}). Nothing is shown rather than a guess.` };
  }
  return {
    kind: "offline",
    message: online ? "The fetch attempt ledger could not be reached. Check your connection and try again." : "You appear to be offline. Reconnect and try again.",
  };
}

export async function listFetchAttempts(sourceId: string): Promise<FetchAttempt[]> {
  return parseFetchAttempts(await apiGet<unknown>(`${TENANT_FETCH_ATTEMPTS_PATH}?sourceId=${encodeURIComponent(sourceId)}`));
}
