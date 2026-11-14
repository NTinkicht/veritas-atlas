import { apiGet, apiPost } from "./http";

// VER-P0-17: client for the tenant publication revision API (VER-P0-16,
// /api/v1/tenant-publication-revisions). The server alone decides authorization, legality,
// self-approval denial and version races; this client never predicts success. No seeded or
// cached fallback, and a malformed response is rejected whole (fail closed).

export const REVISION_STATES = ["Draft", "Submitted", "ChangesRequested", "Approved", "Published", "Withdrawn"] as const;
export type RevisionState = (typeof REVISION_STATES)[number];

export const LIFECYCLE_ACTIONS = ["Submit", "RequestChanges", "Approve", "Revise", "Publish", "Withdraw"] as const;
export type LifecycleAction = (typeof LIFECYCLE_ACTIONS)[number];

export type PublicationRevision = {
  id: string;
  authorId: string;
  contentDigest: string;
  approvedDigest: string | null;
  policyPermitsPublicDisplay: boolean;
  state: RevisionState;
  version: number;
  factsVersion: number;
};

export const REVISIONS_PATH = "/api/v1/tenant-publication-revisions";
const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const DIGEST = /^[0-9a-f]{64}$/;

export class MalformedRevisionResponse extends Error {
  constructor(detail: string) {
    super(`The publication service returned an unexpected response (${detail}).`);
    this.name = "MalformedRevisionResponse";
  }
}

export function isRevisionId(value: string): boolean {
  return GUID.test(value);
}

export function revisionPath(id: string): string {
  if (!GUID.test(id)) throw new MalformedRevisionResponse("revision id is not a GUID");
  return `${REVISIONS_PATH}/${id}`;
}

export function transitionPath(id: string): string {
  return `${revisionPath(id)}/transitions`;
}

export function buildTransitionBody(action: LifecycleAction, expectedVersion: number, operationId: string) {
  if (!LIFECYCLE_ACTIONS.includes(action)) throw new MalformedRevisionResponse("unknown action");
  if (!Number.isSafeInteger(expectedVersion) || expectedVersion < 0) throw new MalformedRevisionResponse("version");
  if (operationId.length === 0 || operationId.length > 200) throw new MalformedRevisionResponse("operation id");
  return { action, expectedVersion, operationId };
}

function integer(record: Record<string, unknown>, key: string): number {
  const value = record[key];
  if (typeof value !== "number" || !Number.isSafeInteger(value) || value < 0) throw new MalformedRevisionResponse(key);
  return value;
}

export function parseRevision(raw: unknown): PublicationRevision {
  if (typeof raw !== "object" || raw === null) throw new MalformedRevisionResponse("not an object");
  const r = raw as Record<string, unknown>;
  const id = r.id;
  if (typeof id !== "string" || !GUID.test(id)) throw new MalformedRevisionResponse("id");
  const authorId = r.authorId;
  if (typeof authorId !== "string" || authorId.length === 0) throw new MalformedRevisionResponse("authorId");
  const contentDigest = r.contentDigest;
  if (typeof contentDigest !== "string" || !DIGEST.test(contentDigest)) throw new MalformedRevisionResponse("contentDigest");
  const approved = r.approvedDigest;
  if (approved !== null && approved !== undefined && (typeof approved !== "string" || !DIGEST.test(approved))) {
    throw new MalformedRevisionResponse("approvedDigest");
  }
  if (typeof r.policyPermitsPublicDisplay !== "boolean") throw new MalformedRevisionResponse("policyPermitsPublicDisplay");
  const state = r.state;
  if (typeof state !== "string" || !(REVISION_STATES as readonly string[]).includes(state)) throw new MalformedRevisionResponse("state");
  return {
    id,
    authorId,
    contentDigest,
    approvedDigest: typeof approved === "string" ? approved : null,
    policyPermitsPublicDisplay: r.policyPermitsPublicDisplay,
    state: state as RevisionState,
    version: integer(r, "version"),
    factsVersion: r.factsVersion === undefined ? 0 : integer(r, "factsVersion"),
  };
}

export function parseTransitionResponse(raw: unknown): { replayed: boolean; revision: PublicationRevision } {
  if (typeof raw !== "object" || raw === null) throw new MalformedRevisionResponse("not an object");
  const r = raw as Record<string, unknown>;
  if (typeof r.replayed !== "boolean") throw new MalformedRevisionResponse("replayed");
  return { replayed: r.replayed, revision: parseRevision(r.revision) };
}

export type RevisionFailureKind = "unauthenticated" | "forbidden" | "not-found" | "conflict" | "offline" | "server" | "malformed";
export type RevisionFailure = { kind: RevisionFailureKind; message: string };

export function classifyRevisionFailure(error: unknown, online: boolean = true): RevisionFailure {
  if (error instanceof MalformedRevisionResponse) return { kind: "malformed", message: error.message };
  const status = typeof error === "object" && error !== null ? (error as { status?: unknown }).status : undefined;
  if (status === 401) return { kind: "unauthenticated", message: "Your session has ended or is not valid. Sign in again." };
  if (status === 403) return { kind: "forbidden", message: "Your role is not permitted to do that to this revision. Nothing was changed." };
  if (status === 404) return { kind: "not-found", message: "That publication revision was not found for your organization." };
  if (status === 409) return { kind: "conflict", message: "The revision changed since you opened it. Nothing was changed; reload it and try again." };
  if (typeof status === "number") {
    return { kind: "server", message: `The publication service failed (HTTP ${status}). Nothing was changed as far as this page can tell.` };
  }
  return {
    kind: "offline",
    message: online ? "The publication service could not be reached. Check your connection and try again." : "You appear to be offline. Reconnect and try again.",
  };
}

export async function getRevision(id: string): Promise<PublicationRevision> {
  return parseRevision(await apiGet<unknown>(revisionPath(id)));
}

export async function transitionRevision(
  id: string,
  action: LifecycleAction,
  expectedVersion: number,
  operationId: string,
): Promise<{ replayed: boolean; revision: PublicationRevision }> {
  const body = buildTransitionBody(action, expectedVersion, operationId);
  return parseTransitionResponse(await apiPost<unknown, typeof body>(transitionPath(id), body));
}
