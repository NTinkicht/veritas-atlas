import { apiGet } from "./http";

// VER-P0-18 / VER-P0-17 (#374): client for the admin-only, read-only audit ledger list
// (GET /api/v1/tenant-audit-events). There is no write verb. No seeded fallback; a malformed
// response is rejected whole and server text is never echoed.

export const AUDIT_EVENTS_PATH = "/api/v1/tenant-audit-events";
const MAX_ENTITY_TYPE = 128;
const MAX_ENTITY_ID = 256;

export type AuditEvent = {
  id: string;
  action: string;
  entityType: string;
  entityId: string;
  actorSubject: string;
  occurredAtUtc: string;
};

export type AuditEventFilter = { entityType?: string; entityId?: string };

export class MalformedAuditEventsResponse extends Error {
  constructor(detail: string) {
    super(`The audit events returned an unexpected response (${detail}).`);
    this.name = "MalformedAuditEventsResponse";
  }
}

export class InvalidAuditEventFilter extends Error {
  constructor(detail: string) {
    super(detail);
    this.name = "InvalidAuditEventFilter";
  }
}

const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
// eslint-disable-next-line no-control-regex
const CONTROL = /[\u0000-\u001f\u007f-\u009f]/;

function text(r: Record<string, unknown>, key: string, index: number): string {
  const v = r[key];
  if (typeof v !== "string" || v.trim() === "")
    throw new MalformedAuditEventsResponse(`event ${index} ${key}`);
  return v;
}

export function parseAuditEvents(raw: unknown): AuditEvent[] {
  if (!Array.isArray(raw)) throw new MalformedAuditEventsResponse("list");
  return raw.map((item: unknown, i): AuditEvent => {
    if (typeof item !== "object" || item === null || Array.isArray(item))
      throw new MalformedAuditEventsResponse(`event ${i}`);
    const r = item as Record<string, unknown>;
    const id = text(r, "id", i);
    if (!GUID.test(id)) throw new MalformedAuditEventsResponse(`event ${i} id`);
    const at = text(r, "occurredAtUtc", i);
    if (Number.isNaN(Date.parse(at)))
      throw new MalformedAuditEventsResponse(`event ${i} occurredAtUtc`);
    return {
      id,
      action: text(r, "action", i),
      entityType: text(r, "entityType", i),
      entityId: text(r, "entityId", i),
      actorSubject: text(r, "actorSubject", i),
      occurredAtUtc: at,
    };
  });
}

function cleanFilter(
  value: string | undefined,
  max: number,
  name: string,
): string | undefined {
  if (value === undefined || value.trim() === "") return undefined;
  if (value.length > max || CONTROL.test(value))
    throw new InvalidAuditEventFilter(
      `${name} must be a short identifier without control characters.`,
    );
  return value;
}

// Mirrors the server: an entityId filter needs an entityType. Throws before any request is made.
export function auditEventsPath(filter: AuditEventFilter = {}): string {
  const entityType = cleanFilter(
    filter.entityType,
    MAX_ENTITY_TYPE,
    "Entity type",
  );
  const entityId = cleanFilter(filter.entityId, MAX_ENTITY_ID, "Entity id");
  if (entityId !== undefined && entityType === undefined)
    throw new InvalidAuditEventFilter(
      "An entity id filter requires an entity type.",
    );
  const query = new URLSearchParams();
  if (entityType !== undefined) query.set("entityType", entityType);
  if (entityId !== undefined) query.set("entityId", entityId);
  const q = query.toString();
  return q ? `${AUDIT_EVENTS_PATH}?${q}` : AUDIT_EVENTS_PATH;
}

export type AuditEventsFailureKind =
  | "invalid"
  | "unauthenticated"
  | "forbidden"
  | "bad-request"
  | "offline"
  | "server"
  | "malformed";
export type AuditEventsFailure = {
  kind: AuditEventsFailureKind;
  message: string;
};

// Fixed messages only: server text is never echoed.
export function classifyAuditEventsFailure(
  error: unknown,
  online: boolean = true,
): AuditEventsFailure {
  if (error instanceof InvalidAuditEventFilter)
    return { kind: "invalid", message: error.message };
  if (error instanceof MalformedAuditEventsResponse)
    return { kind: "malformed", message: error.message };
  const status =
    typeof error === "object" && error !== null
      ? (error as { status?: unknown }).status
      : undefined;
  if (status === 401)
    return {
      kind: "unauthenticated",
      message: "Your session has ended or is not valid. Sign in again.",
    };
  if (status === 403)
    return {
      kind: "forbidden",
      message: "Only an organization administrator may read audit events.",
    };
  if (status === 400)
    return {
      kind: "bad-request",
      message: "The audit event filter was not accepted.",
    };
  if (typeof status === "number") {
    return {
      kind: "server",
      message: `The audit events failed to load (HTTP ${status}). Nothing is shown rather than a guess.`,
    };
  }
  return {
    kind: "offline",
    message: online
      ? "The audit events could not be reached. Check your connection and try again."
      : "You appear to be offline. Reconnect and try again.",
  };
}

export async function listAuditEvents(
  filter: AuditEventFilter = {},
): Promise<AuditEvent[]> {
  return parseAuditEvents(await apiGet<unknown>(auditEventsPath(filter)));
}
