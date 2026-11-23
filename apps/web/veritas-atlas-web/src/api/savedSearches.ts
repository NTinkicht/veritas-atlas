import { apiDelete, apiGet, apiPost } from "./http";
import { toFreshnessLabel, type FreshnessLabel } from "./evidenceSearch";

// VER-P1-01 (#394): client for the private saved evidence searches API. Only search criteria are
// stored; running one goes through the normal evidence search, which re-evaluates freshness per request.

export const SAVED_SEARCHES_PATH = "/api/v1/tenant-saved-searches";
export const SAVED_SEARCH_MAX_NAME = 100;

export type SavedSearch = {
  id: string;
  name: string;
  query: string;
  freshness: FreshnessLabel | null;
  jurisdiction: string | null;
  sourceType: string | null;
  version: number;
  // True when the saved search uses a criterion the search page cannot express (source entry, parser, subject term).
  hasUnsupportedCriteria: boolean;
};

export type SavedSearchDraft = { name: string; q: string; freshness: FreshnessLabel | ""; jurisdiction: string; type: string };

export class MalformedSavedSearchResponse extends Error {
  constructor(detail: string) {
    super(`The saved search service returned an unexpected response (${detail}).`);
    this.name = "MalformedSavedSearchResponse";
  }
}

function text(record: Record<string, unknown>, key: string, required: boolean): string | null {
  const value = record[key];
  if (value === null || value === undefined) {
    if (required) throw new MalformedSavedSearchResponse(`${key} missing`);
    return null;
  }
  if (typeof value !== "string") throw new MalformedSavedSearchResponse(`${key} is not text`);
  return value;
}

export function parseSavedSearch(item: unknown): SavedSearch {
  if (typeof item !== "object" || item === null) throw new MalformedSavedSearchResponse("not an object");
  const r = item as Record<string, unknown>;
  if (typeof r.version !== "number" || !Number.isInteger(r.version)) throw new MalformedSavedSearchResponse("version");
  const rawFreshness = text(r, "freshness", false);
  const freshness = rawFreshness === null ? null : toFreshnessLabel(rawFreshness);
  // An unrecognised freshness value is never applied as a guess: treat the search as not applicable.
  const freshnessUnknown = rawFreshness !== null && freshness === "UNKNOWN" && rawFreshness.trim().toLowerCase() !== "unknown";
  const unsupported = ["sourceEntryId", "parserName", "parserVersion", "subjectTerm"].some(
    (key) => r[key] !== null && r[key] !== undefined,
  );
  return {
    id: text(r, "id", true) as string,
    name: text(r, "name", true) as string,
    query: text(r, "query", true) as string,
    freshness,
    jurisdiction: text(r, "jurisdiction", false),
    sourceType: text(r, "sourceType", false),
    version: r.version,
    hasUnsupportedCriteria: unsupported || freshnessUnknown,
  };
}

// Fail closed: one malformed row rejects the whole list.
export function parseSavedSearchList(raw: unknown): SavedSearch[] {
  if (typeof raw !== "object" || raw === null || !Array.isArray((raw as { items?: unknown }).items)) {
    throw new MalformedSavedSearchResponse("items missing");
  }
  return (raw as { items: unknown[] }).items.map(parseSavedSearch);
}

export function isValidSavedSearchName(name: string): boolean {
  return (
    name.length >= 1 &&
    name.length <= SAVED_SEARCH_MAX_NAME &&
    name === name.trim() &&
    // eslint-disable-next-line no-control-regex
    !/[\u0000-\u001f\u007f-\u009f]/.test(name)
  );
}

export type SavedSearchFailure = {
  kind: "unauthenticated" | "forbidden" | "name-taken" | "limit" | "invalid" | "server" | "offline" | "malformed";
  message: string;
};

export function classifySavedSearchFailure(error: unknown, online: boolean = true): SavedSearchFailure {
  if (error instanceof MalformedSavedSearchResponse) return { kind: "malformed", message: error.message };
  const status = typeof error === "object" && error !== null ? (error as { status?: unknown }).status : undefined;
  const raw = typeof error === "object" && error !== null ? (error as { raw?: unknown }).raw : undefined;
  const code = typeof raw === "object" && raw !== null ? (raw as { code?: unknown }).code : undefined;
  if (status === 401) return { kind: "unauthenticated", message: "Your session has ended or is not valid. Sign in again." };
  if (status === 403) {
    return { kind: "forbidden", message: "Only an operator or analyst may manage saved searches for an organization." };
  }
  if (status === 409 && code === "saved_search_name_taken") {
    return { kind: "name-taken", message: "A saved search with this name already exists. Choose another name." };
  }
  if (status === 409) return { kind: "limit", message: "You have reached the saved search limit. Delete one first." };
  if (status === 400) return { kind: "invalid", message: "The saved search was rejected: check the name and search criteria." };
  if (typeof status === "number") {
    return { kind: "server", message: `The saved search service failed (HTTP ${status}). Nothing is shown rather than a guess.` };
  }
  return {
    kind: "offline",
    message: online ? "The saved search service could not be reached. Check your connection and try again." : "You appear to be offline. Reconnect and try again.",
  };
}

export async function listSavedSearches(): Promise<SavedSearch[]> {
  return parseSavedSearchList(await apiGet<unknown>(SAVED_SEARCHES_PATH));
}

export async function createSavedSearch(draft: SavedSearchDraft): Promise<SavedSearch> {
  const body = {
    name: draft.name,
    query: draft.q.trim(),
    freshness: draft.freshness || null,
    jurisdiction: draft.jurisdiction.trim() || null,
    sourceType: draft.type.trim() || null,
  };
  return parseSavedSearch(await apiPost<unknown>(SAVED_SEARCHES_PATH, body));
}

export async function deleteSavedSearch(id: string): Promise<void> {
  await apiDelete(`${SAVED_SEARCHES_PATH}/${encodeURIComponent(id)}`);
}
