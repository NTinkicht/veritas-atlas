import { apiGet, apiPost } from "./http";

// VER-P1-02 (#400): client for the append-only editorial comment history of a publication revision.
// The server is the authority: tenant and author come from the token, the body is re-validated there.

export const COMMENT_MAX_BODY = 2000;

export type RevisionComment = { id: string; author: string; body: string; createdAtUtc: string };

export class MalformedCommentResponse extends Error {
  constructor(detail: string) {
    super(`The comment service returned an unexpected response (${detail}).`);
    this.name = "MalformedCommentResponse";
  }
}

const commentsPath = (revisionId: string) => `/api/v1/tenant-publication-revisions/${encodeURIComponent(revisionId)}/comments`;

function requiredText(record: Record<string, unknown>, key: string): string {
  const value = record[key];
  if (typeof value !== "string" || value.length === 0) throw new MalformedCommentResponse(`${key} missing`);
  return value;
}

export function parseComment(item: unknown): RevisionComment {
  if (typeof item !== "object" || item === null) throw new MalformedCommentResponse("not an object");
  const r = item as Record<string, unknown>;
  const createdAtUtc = requiredText(r, "createdAtUtc");
  if (Number.isNaN(Date.parse(createdAtUtc))) throw new MalformedCommentResponse("createdAtUtc");
  return { id: requiredText(r, "id"), author: requiredText(r, "author"), body: requiredText(r, "body"), createdAtUtc };
}

// Fail closed: one malformed row rejects the whole history.
export function parseCommentList(raw: unknown): RevisionComment[] {
  if (typeof raw !== "object" || raw === null || !Array.isArray((raw as { comments?: unknown }).comments)) {
    throw new MalformedCommentResponse("comments missing");
  }
  return (raw as { comments: unknown[] }).comments.map(parseComment);
}

// Mirrors the server rule: 1..2000 characters after trimming, no control characters except line breaks and tabs.
export function normalizeCommentBody(raw: string): string | null {
  const body = raw.trim();
  // eslint-disable-next-line no-control-regex
  if (body.length === 0 || body.length > COMMENT_MAX_BODY || /[\u0000-\u0008\u000b\u000c\u000e-\u001f\u007f-\u009f]/.test(body)) return null;
  return body;
}

export type CommentFailure = {
  kind: "unauthenticated" | "forbidden" | "not-found" | "limit" | "key-conflict" | "invalid" | "server" | "offline" | "malformed";
  message: string;
};

export function classifyCommentFailure(error: unknown, online: boolean = true): CommentFailure {
  if (error instanceof MalformedCommentResponse) return { kind: "malformed", message: error.message };
  const status = typeof error === "object" && error !== null ? (error as { status?: unknown }).status : undefined;
  const raw = typeof error === "object" && error !== null ? (error as { raw?: unknown }).raw : undefined;
  const code = typeof raw === "object" && raw !== null ? (raw as { code?: unknown }).code : undefined;
  if (status === 401) return { kind: "unauthenticated", message: "Your session has ended or is not valid. Sign in again." };
  if (status === 403) return { kind: "forbidden", message: "Your role may not use publication comments." };
  if (status === 404) return { kind: "not-found", message: "The publication revision was not found." };
  if (status === 409 && code === "comment_limit_reached") return { kind: "limit", message: "This revision has reached its comment limit." };
  if (status === 409) return { kind: "key-conflict", message: "This comment could not be saved as sent. Edit it and try again." };
  if (status === 400) return { kind: "invalid", message: "The comment was rejected: check its length and characters." };
  if (typeof status === "number") {
    return { kind: "server", message: `The comment service failed (HTTP ${status}). Nothing is shown rather than a guess.` };
  }
  return {
    kind: "offline",
    message: online ? "The comment service could not be reached. Check your connection and try again." : "You appear to be offline. Reconnect and try again.",
  };
}

export async function listComments(revisionId: string): Promise<RevisionComment[]> {
  return parseCommentList(await apiGet<unknown>(commentsPath(revisionId)));
}

export async function addComment(revisionId: string, clientKey: string, body: string): Promise<RevisionComment> {
  return parseComment(await apiPost<unknown>(commentsPath(revisionId), { clientKey, body }));
}
