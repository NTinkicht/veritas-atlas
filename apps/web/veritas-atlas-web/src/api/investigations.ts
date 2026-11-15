import { apiDelete, apiGet, apiPost, apiPut } from "./http";

// VER-P0-17: read-only client for the tenant-scoped investigation workspace API
// (VER-P0-10, /api/v1/tenant-investigations). No seeded or cached fallback: what is shown is
// exactly what the API returned, and a malformed response is rejected whole (fail closed).

export type Investigation = {
  id: string;
  title: string;
  createdBySubject: string;
  createdAtUtc: string;
};

export type CollectedEvidenceItem = {
  id: string;
  evidenceVerificationId: string;
  note: string | null;
  collectedBySubject: string;
  createdAtUtc: string;
};

export const INVESTIGATIONS_PATH = "/api/v1/tenant-investigations";
const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

export class MalformedInvestigationResponse extends Error {
  constructor(detail: string) {
    super(`The investigation service returned an unexpected response (${detail}).`);
    this.name = "MalformedInvestigationResponse";
  }
}

export function evidenceItemsPath(investigationId: string): string {
  if (!GUID.test(investigationId)) throw new MalformedInvestigationResponse("investigation id is not a GUID");
  return `${INVESTIGATIONS_PATH}/${investigationId}/evidence-items`;
}

function items(raw: unknown): Record<string, unknown>[] {
  if (typeof raw !== "object" || raw === null) throw new MalformedInvestigationResponse("not an object");
  const list = (raw as Record<string, unknown>).items;
  if (!Array.isArray(list)) throw new MalformedInvestigationResponse("items missing");
  return list.map((item, index) => {
    if (typeof item !== "object" || item === null) throw new MalformedInvestigationResponse(`item ${index}`);
    return item as Record<string, unknown>;
  });
}

function text(record: Record<string, unknown>, key: string, index: number, label: string = "item"): string {
  const value = record[key];
  if (typeof value !== "string" || value.length === 0) throw new MalformedInvestigationResponse(`${label} ${index} ${key}`);
  return value;
}

function guid(record: Record<string, unknown>, key: string, index: number, label: string = "item"): string {
  const value = text(record, key, index, label);
  if (!GUID.test(value)) throw new MalformedInvestigationResponse(`${label} ${index} ${key}`);
  return value;
}

export function parseInvestigations(raw: unknown): Investigation[] {
  return items(raw).map((r, i) => ({
    id: guid(r, "id", i),
    title: text(r, "title", i),
    createdBySubject: text(r, "createdBySubject", i),
    createdAtUtc: text(r, "createdAtUtc", i),
  }));
}

export function parseEvidenceItems(raw: unknown): CollectedEvidenceItem[] {
  return items(raw).map((r, i) => {
    const note = r.note;
    if (note !== null && note !== undefined && typeof note !== "string") {
      throw new MalformedInvestigationResponse(`item ${i} note`);
    }
    return {
      id: guid(r, "id", i),
      evidenceVerificationId: guid(r, "evidenceVerificationId", i),
      note: typeof note === "string" && note.length > 0 ? note : null,
      collectedBySubject: text(r, "collectedBySubject", i),
      createdAtUtc: text(r, "createdAtUtc", i),
    };
  });
}

export type InvestigationFailureKind = "unauthenticated" | "forbidden" | "not-found" | "offline" | "server" | "malformed";
export type InvestigationFailure = { kind: InvestigationFailureKind; message: string };

export function classifyInvestigationFailure(error: unknown, online: boolean = true): InvestigationFailure {
  if (error instanceof MalformedInvestigationResponse) return { kind: "malformed", message: error.message };
  const status = typeof error === "object" && error !== null ? (error as { status?: unknown }).status : undefined;
  if (status === 401) return { kind: "unauthenticated", message: "Your session has ended or is not valid. Sign in again." };
  if (status === 403) return { kind: "forbidden", message: "Your account is not permitted to do that in this organization's investigations." };
  if (status === 404) return { kind: "not-found", message: "That investigation was not found or is not shared with you." };
  if (typeof status === "number") {
    return { kind: "server", message: `The investigation service failed (HTTP ${status}). Nothing is shown rather than a guess.` };
  }
  return {
    kind: "offline",
    message: online ? "The investigation service could not be reached. Check your connection and try again." : "You appear to be offline. Reconnect and try again.",
  };
}

export async function listInvestigations(): Promise<Investigation[]> {
  return parseInvestigations(await apiGet<unknown>(INVESTIGATIONS_PATH));
}

export async function listEvidenceItems(investigationId: string): Promise<CollectedEvidenceItem[]> {
  return parseEvidenceItems(await apiGet<unknown>(evidenceItemsPath(investigationId)));
}

export type ComparedEvidence = {
  itemId: string;
  evidenceVerificationId: string;
  note: string | null;
  parentEvidenceRef: string;
  rawRetrievalId: string;
  attestationId: string;
  parseRunId: string;
  verifiedAtUtc: string;
};

export type EvidenceComparison = {
  left: ComparedEvidence;
  right: ComparedEvidence;
  sameEvidenceRef: boolean;
  sameRawRetrieval: boolean;
  sameAttestation: boolean;
  sameParseRun: boolean;
};

export function comparisonPath(investigationId: string, leftItemId: string, rightItemId: string): string {
  if (!GUID.test(leftItemId) || !GUID.test(rightItemId)) throw new MalformedInvestigationResponse("item id is not a GUID");
  if (leftItemId.toLowerCase() === rightItemId.toLowerCase()) throw new MalformedInvestigationResponse("items must differ");
  return `${evidenceItemsPath(investigationId)}/comparison?left=${leftItemId}&right=${rightItemId}`;
}

function side(raw: unknown, name: string): ComparedEvidence {
  if (typeof raw !== "object" || raw === null) throw new MalformedInvestigationResponse(`${name} missing`);
  const r = raw as Record<string, unknown>;
  const note = r.note;
  if (note !== null && note !== undefined && typeof note !== "string") throw new MalformedInvestigationResponse(`${name} note`);
  const at = `${name}`;
  return {
    itemId: guid(r, "itemId", 0, at),
    evidenceVerificationId: guid(r, "evidenceVerificationId", 0, at),
    note: typeof note === "string" && note.length > 0 ? note : null,
    parentEvidenceRef: text(r, "parentEvidenceRef", 0, at),
    rawRetrievalId: guid(r, "rawRetrievalId", 0, at),
    attestationId: guid(r, "attestationId", 0, at),
    parseRunId: guid(r, "parseRunId", 0, at),
    verifiedAtUtc: text(r, "verifiedAtUtc", 0, at),
  };
}

function flag(record: Record<string, unknown>, key: string): boolean {
  const value = record[key];
  if (typeof value !== "boolean") throw new MalformedInvestigationResponse(key);
  return value;
}

export function parseEvidenceComparison(raw: unknown): EvidenceComparison {
  if (typeof raw !== "object" || raw === null) throw new MalformedInvestigationResponse("not an object");
  const r = raw as Record<string, unknown>;
  const left = side(r.left, "left");
  const right = side(r.right, "right");
  const comparison = {
    left,
    right,
    sameEvidenceRef: flag(r, "sameEvidenceRef"),
    sameRawRetrieval: flag(r, "sameRawRetrieval"),
    sameAttestation: flag(r, "sameAttestation"),
    sameParseRun: flag(r, "sameParseRun"),
  };
  // Fail closed on a response whose flags contradict its own sides.
  if (
    comparison.sameEvidenceRef !== (left.parentEvidenceRef === right.parentEvidenceRef) ||
    comparison.sameRawRetrieval !== (left.rawRetrievalId === right.rawRetrievalId) ||
    comparison.sameAttestation !== (left.attestationId === right.attestationId) ||
    comparison.sameParseRun !== (left.parseRunId === right.parseRunId)
  ) {
    throw new MalformedInvestigationResponse("comparison flags contradict the compared items");
  }
  return comparison;
}

export async function compareEvidenceItems(
  investigationId: string,
  leftItemId: string,
  rightItemId: string,
): Promise<EvidenceComparison> {
  return parseEvidenceComparison(await apiGet<unknown>(comparisonPath(investigationId, leftItemId, rightItemId)));
}

export type EvidenceBookmark = { investigationId: string; evidenceItemId: string; createdAtUtc: string };

export function bookmarksPath(investigationId: string): string {
  if (!GUID.test(investigationId)) throw new MalformedInvestigationResponse("investigation id is not a GUID");
  return `${INVESTIGATIONS_PATH}/${investigationId}/bookmarks`;
}

export function bookmarkPath(investigationId: string, itemId: string): string {
  if (!GUID.test(itemId)) throw new MalformedInvestigationResponse("item id is not a GUID");
  return `${evidenceItemsPath(investigationId)}/${itemId}/bookmark`;
}

function toBookmark(r: Record<string, unknown>, index: number): EvidenceBookmark {
  return {
    investigationId: guid(r, "investigationId", index, "bookmark"),
    evidenceItemId: guid(r, "evidenceItemId", index, "bookmark"),
    createdAtUtc: text(r, "createdAtUtc", index, "bookmark"),
  };
}

export function parseBookmarks(raw: unknown): EvidenceBookmark[] {
  return items(raw).map(toBookmark);
}

export function parseBookmark(raw: unknown): EvidenceBookmark {
  if (typeof raw !== "object" || raw === null) throw new MalformedInvestigationResponse("bookmark is not an object");
  return toBookmark(raw as Record<string, unknown>, 0);
}

export async function listBookmarks(investigationId: string): Promise<EvidenceBookmark[]> {
  return parseBookmarks(await apiGet<unknown>(bookmarksPath(investigationId)));
}

/** Idempotent on the server: bookmarking an already bookmarked item replays the original row. */
export async function putBookmark(investigationId: string, itemId: string): Promise<EvidenceBookmark> {
  const bookmark = parseBookmark(await apiPut<unknown>(bookmarkPath(investigationId, itemId)));
  // Fail closed on an answer about some other item or investigation.
  if (
    bookmark.evidenceItemId.toLowerCase() !== itemId.toLowerCase() ||
    bookmark.investigationId.toLowerCase() !== investigationId.toLowerCase()
  ) {
    throw new MalformedInvestigationResponse("bookmark does not match the requested item");
  }
  return bookmark;
}

export async function removeBookmark(investigationId: string, itemId: string): Promise<void> {
  await apiDelete(bookmarkPath(investigationId, itemId));
}

export type EvidenceAnnotation = {
  id: string;
  investigationId: string;
  evidenceItemId: string;
  text: string;
  authorSubject: string;
  createdAtUtc: string;
};

/** Mirrors TenantInvestigationAnnotationLimits on the server. */
export const ANNOTATION_MAX_TEXT_LENGTH = 1000;

/** Client-side mirror of the server text rule so invalid text never reaches the network. */
export function isValidAnnotationText(value: string): boolean {
  if (value.length === 0 || value.length > ANNOTATION_MAX_TEXT_LENGTH) return false;
  if (value.trim().length === 0 || value !== value.trim()) return false;
  // eslint-disable-next-line no-control-regex
  return !/[\u0000-\u0009\u000b-\u001f\u007f-\u009f]/.test(value);
}

export function annotationsPath(investigationId: string, itemId: string): string {
  if (!GUID.test(itemId)) throw new MalformedInvestigationResponse("item id is not a GUID");
  return `${evidenceItemsPath(investigationId)}/${itemId}/annotations`;
}

function toAnnotation(r: Record<string, unknown>, index: number): EvidenceAnnotation {
  return {
    id: guid(r, "id", index, "annotation"),
    investigationId: guid(r, "investigationId", index, "annotation"),
    evidenceItemId: guid(r, "evidenceItemId", index, "annotation"),
    text: text(r, "text", index, "annotation"),
    authorSubject: text(r, "authorSubject", index, "annotation"),
    createdAtUtc: text(r, "createdAtUtc", index, "annotation"),
  };
}

export function parseAnnotations(raw: unknown): EvidenceAnnotation[] {
  return items(raw).map(toAnnotation);
}

export function parseAnnotation(raw: unknown): EvidenceAnnotation {
  if (typeof raw !== "object" || raw === null) throw new MalformedInvestigationResponse("annotation is not an object");
  return toAnnotation(raw as Record<string, unknown>, 0);
}

function sameIds(a: EvidenceAnnotation, investigationId: string, itemId: string): boolean {
  return (
    a.investigationId.toLowerCase() === investigationId.toLowerCase() &&
    a.evidenceItemId.toLowerCase() === itemId.toLowerCase()
  );
}

export async function listAnnotations(investigationId: string, itemId: string): Promise<EvidenceAnnotation[]> {
  const list = parseAnnotations(await apiGet<unknown>(annotationsPath(investigationId, itemId)));
  // Fail closed on a list that mentions some other item or investigation.
  if (!list.every((a) => sameIds(a, investigationId, itemId))) {
    throw new MalformedInvestigationResponse("annotation does not match the requested item");
  }
  return list;
}

/** Append-only: the server never edits or deletes. Invalid text is refused before any request. */
export async function addAnnotation(investigationId: string, itemId: string, value: string): Promise<EvidenceAnnotation> {
  const path = annotationsPath(investigationId, itemId);
  if (!isValidAnnotationText(value)) throw new MalformedInvestigationResponse("annotation text is not valid");
  const annotation = parseAnnotation(await apiPost<unknown, { text: string }>(path, { text: value }));
  if (!sameIds(annotation, investigationId, itemId) || annotation.text !== value) {
    throw new MalformedInvestigationResponse("annotation does not match the request");
  }
  return annotation;
}
