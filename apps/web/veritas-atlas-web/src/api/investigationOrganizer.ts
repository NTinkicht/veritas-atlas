import { apiDelete, apiGet, apiPut, apiPutBody } from "./http";

// VER-P1-01: client for the private investigation tags and folder APIs. Both are the caller's own
// organizing metadata; the server is the authority for sharing, closed state and limits. Nothing here
// is applied to the view until the server confirms it.

export const TAG_MAX_LENGTH = 40;
export const FOLDER_MAX_LENGTH = 60;

export type OrganizerFailure = {
  kind: "unauthenticated" | "forbidden" | "not-found" | "closed" | "limit" | "invalid" | "server" | "offline" | "malformed";
  message: string;
};

export class MalformedOrganizerResponse extends Error {
  constructor(detail: string) {
    super(`The organizer service returned an unexpected response (${detail}).`);
    this.name = "MalformedOrganizerResponse";
  }
}

const base = (id: string) => `/api/v1/tenant-investigations/${encodeURIComponent(id)}`;

/** The server lowercases tags; mirror the rule so a doomed request is never sent. Returns the normalized tag or null. */
export function normalizeTag(raw: string): string | null {
  const tag = raw.toLowerCase();
  return /^[a-z0-9][a-z0-9_.-]{0,39}$/.test(tag) ? tag : null;
}

/** Trims, then 1..60 of ASCII letters, digits, single inner spaces, '_', '.', '-'. Returns the normalized name or null. */
export function normalizeFolderName(raw: string): string | null {
  const name = raw.trim();
  if (name.length < 1 || name.length > FOLDER_MAX_LENGTH) return null;
  return /^[A-Za-z0-9_.-]+(?: [A-Za-z0-9_.-]+)*$/.test(name) ? name : null;
}

function tagOf(item: unknown): string {
  if (typeof item !== "object" || item === null) throw new MalformedOrganizerResponse("tag is not an object");
  const tag = (item as { tag?: unknown }).tag;
  if (typeof tag !== "string" || normalizeTag(tag) !== tag) throw new MalformedOrganizerResponse("tag invalid");
  return tag;
}

// Fail closed: one malformed row rejects the whole list.
export function parseTagList(raw: unknown): string[] {
  const items = typeof raw === "object" && raw !== null ? (raw as { items?: unknown }).items : undefined;
  if (!Array.isArray(items)) throw new MalformedOrganizerResponse("items missing");
  return items.map(tagOf);
}

export function parseFolder(raw: unknown): string {
  const name = typeof raw === "object" && raw !== null ? (raw as { name?: unknown }).name : undefined;
  if (typeof name !== "string" || normalizeFolderName(name) !== name) throw new MalformedOrganizerResponse("folder name invalid");
  return name;
}

export function classifyOrganizerFailure(error: unknown, online: boolean = true): OrganizerFailure {
  if (error instanceof MalformedOrganizerResponse) return { kind: "malformed", message: error.message };
  const status = typeof error === "object" && error !== null ? (error as { status?: unknown }).status : undefined;
  const raw = typeof error === "object" && error !== null ? (error as { raw?: unknown }).raw : undefined;
  const code = typeof raw === "object" && raw !== null ? (raw as { code?: unknown }).code : undefined;
  if (status === 401) return { kind: "unauthenticated", message: "Your session has ended or is not valid. Sign in again." };
  if (status === 403) {
    return { kind: "forbidden", message: "Only an operator or analyst may change tags and folders; other roles may only read them." };
  }
  if (status === 404) return { kind: "not-found", message: "The investigation or tag was not found; reload the list." };
  if (status === 409 && code === "investigation_closed") {
    return { kind: "closed", message: "The investigation is closed; reopen it to change its tags or folder." };
  }
  if (status === 409) return { kind: "limit", message: "The tag limit for this investigation has been reached. Remove a tag first." };
  if (status === 400) return { kind: "invalid", message: "The value was rejected: check the characters and length." };
  if (typeof status === "number") {
    return { kind: "server", message: `The organizer service failed (HTTP ${status}). Nothing is shown rather than a guess.` };
  }
  return {
    kind: "offline",
    message: online ? "The organizer service could not be reached. Check your connection and try again." : "You appear to be offline. Reconnect and try again.",
  };
}

export async function listTags(investigationId: string): Promise<string[]> {
  return parseTagList(await apiGet<unknown>(`${base(investigationId)}/tags`));
}

export async function addTag(investigationId: string, tag: string): Promise<string> {
  return tagOf(await apiPut<unknown>(`${base(investigationId)}/tags/${encodeURIComponent(tag)}`));
}

export async function removeTag(investigationId: string, tag: string): Promise<void> {
  await apiDelete(`${base(investigationId)}/tags/${encodeURIComponent(tag)}`);
}

/** The investigation's folder, or null when it is not filed (the server answers 404 investigation_folder_not_set). */
export async function getFolder(investigationId: string): Promise<string | null> {
  try {
    return parseFolder(await apiGet<unknown>(`${base(investigationId)}/folder`));
  } catch (error) {
    const raw = typeof error === "object" && error !== null ? (error as { raw?: unknown }).raw : undefined;
    const code = typeof raw === "object" && raw !== null ? (raw as { code?: unknown }).code : undefined;
    if ((error as { status?: unknown } | null)?.status === 404 && code === "investigation_folder_not_set") return null;
    throw error;
  }
}

export async function setFolder(investigationId: string, name: string): Promise<string> {
  return parseFolder(await apiPutBody<unknown, { name: string }>(`${base(investigationId)}/folder`, { name }));
}

export async function clearFolder(investigationId: string): Promise<void> {
  await apiDelete(`${base(investigationId)}/folder`);
}
