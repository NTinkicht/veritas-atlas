import { apiPost } from "./http";

// VER-P0-17: client for the stateless claim relation graph (VER-P0-13,
// POST /api/v1/tenant-claims/relation-graph). The server serialises both enums as integers, so they are
// mapped explicitly here and any unknown value rejects the whole response (fail closed).
// Evidence references are echoed by the server, never verified. No seeded fallback.

export const RELATION_GRAPH_PATH = "/api/v1/tenant-claims/relation-graph";
export const MAX_CLAIMS = 200;
export const MAX_FIELD_LENGTH = 500;

export type ClaimInput = {
  claimId: string;
  evidenceRef: string;
  sourceId: string;
  subject: string;
  entityKey: string;
  predicate: string;
  negated: boolean;
  value: string;
  asOf: string;
};

export const emptyClaim = (): ClaimInput => ({
  claimId: "", evidenceRef: "", sourceId: "", subject: "", entityKey: "", predicate: "", negated: false, value: "", asOf: "",
});

export const NODE_STATUSES = ["conflicted", "unresolved", "superseded", "agreeing", "isolated"] as const;
export type NodeStatus = (typeof NODE_STATUSES)[number];
export const RELATIONS = ["unrelated", "agreeing", "conflicting", "superseded", "unresolved"] as const;
export type RelationName = (typeof RELATIONS)[number];

export type GraphNode = { claimId: string; evidenceRef: string; sourceId: string | null; status: NodeStatus };
export type GraphEdge = {
  relation: Exclude<RelationName, "unrelated">;
  reason: string;
  firstClaimId: string;
  secondClaimId: string;
  firstEvidenceRef: string;
  secondEvidenceRef: string;
  firstSourceId: string | null;
  secondSourceId: string | null;
  supersededClaimId: string | null;
  supersedingClaimId: string | null;
};
export type RelationGraph = { nodes: GraphNode[]; edges: GraphEdge[] };

export class InvalidGraphInput extends Error {
  constructor(detail: string) {
    super(detail);
    this.name = "InvalidGraphInput";
  }
}

export class MalformedGraphResponse extends Error {
  constructor(detail: string) {
    super(`The relation service returned an unexpected response (${detail}).`);
    this.name = "MalformedGraphResponse";
  }
}

const hasControl = (s: string) => Array.from(s).some((ch) => ch.charCodeAt(0) < 32 || ch.charCodeAt(0) === 127);

function field(label: string, raw: string, required: boolean): string | undefined {
  const v = raw.trim();
  if (v === "") {
    if (required) throw new InvalidGraphInput(`${label} is required.`);
    return undefined;
  }
  if (v.length > MAX_FIELD_LENGTH || hasControl(v)) {
    throw new InvalidGraphInput(`${label} must be at most ${MAX_FIELD_LENGTH} characters with no control characters.`);
  }
  return v;
}

export function buildGraphBody(claims: ClaimInput[]) {
  if (claims.length === 0 || claims.length > MAX_CLAIMS) throw new InvalidGraphInput(`Enter 1-${MAX_CLAIMS} claims.`);
  const seen = new Set<string>();
  const out = claims.map((c, i) => {
    const n = `Claim ${i + 1}`;
    const claimId = field(`${n} id`, c.claimId, true) as string;
    if (seen.has(claimId)) throw new InvalidGraphInput(`${n} id "${claimId}" is used twice.`);
    seen.add(claimId);
    const asOf = c.asOf.trim();
    if (asOf !== "" && !/^\d{4}-\d{2}-\d{2}$/.test(asOf)) throw new InvalidGraphInput(`${n} as-of date must be YYYY-MM-DD or blank.`);
    return {
      claimId,
      evidenceRef: field(`${n} evidence reference`, c.evidenceRef, true) as string,
      sourceId: field(`${n} source id`, c.sourceId, false),
      subject: field(`${n} subject`, c.subject, true) as string,
      entityKey: field(`${n} entity key`, c.entityKey, false),
      predicate: field(`${n} predicate`, c.predicate, true) as string,
      negated: c.negated,
      value: field(`${n} value`, c.value, false),
      asOf: asOf === "" ? undefined : asOf,
    };
  });
  return { claims: out };
}

const str = (v: unknown, what: string): string => {
  if (typeof v !== "string" || v.length === 0) throw new MalformedGraphResponse(what);
  return v;
};
const optStr = (v: unknown, what: string): string | null => {
  if (v === null || v === undefined) return null;
  return str(v, what);
};

function enumOf<T extends string>(raw: unknown, names: readonly T[], what: string): T {
  if (typeof raw === "number" && Number.isInteger(raw) && raw >= 0 && raw < names.length) return names[raw];
  if (typeof raw === "string") {
    const hit = names.find((n) => n === raw.toLowerCase());
    if (hit) return hit;
  }
  throw new MalformedGraphResponse(what);
}

export function parseGraphResponse(raw: unknown, expectedClaimIds: readonly string[]): RelationGraph {
  if (typeof raw !== "object" || raw === null) throw new MalformedGraphResponse("not an object");
  const r = raw as Record<string, unknown>;
  if (r.evidenceVerified !== false) throw new MalformedGraphResponse("evidenceVerified");
  const g = r.graph as Record<string, unknown> | null;
  if (typeof g !== "object" || g === null || !Array.isArray(g.nodes) || !Array.isArray(g.edges)) throw new MalformedGraphResponse("graph");
  const nodes = g.nodes.map((n): GraphNode => {
    const o = n as Record<string, unknown> | null;
    if (typeof o !== "object" || o === null) throw new MalformedGraphResponse("node");
    return {
      claimId: str(o.claimId, "node claimId"),
      evidenceRef: str(o.evidenceRef, "node evidenceRef"),
      sourceId: optStr(o.sourceId, "node sourceId"),
      status: enumOf(o.status, NODE_STATUSES, "node status"),
    };
  });
  const ids = nodes.map((n) => n.claimId).sort();
  const want = [...expectedClaimIds].sort();
  if (ids.length !== want.length || ids.some((id, i) => id !== want[i])) throw new MalformedGraphResponse("node set differs from request");
  const known = new Set(ids);
  const edges = g.edges.map((e): GraphEdge => {
    const o = e as Record<string, unknown> | null;
    if (typeof o !== "object" || o === null) throw new MalformedGraphResponse("edge");
    const relation = enumOf(o.relation, RELATIONS, "edge relation");
    if (relation === "unrelated") throw new MalformedGraphResponse("edge relation");
    const edge: GraphEdge = {
      relation,
      reason: str(o.reason, "edge reason"),
      firstClaimId: str(o.firstClaimId, "edge firstClaimId"),
      secondClaimId: str(o.secondClaimId, "edge secondClaimId"),
      firstEvidenceRef: str(o.firstEvidenceRef, "edge firstEvidenceRef"),
      secondEvidenceRef: str(o.secondEvidenceRef, "edge secondEvidenceRef"),
      firstSourceId: optStr(o.firstSourceId, "edge firstSourceId"),
      secondSourceId: optStr(o.secondSourceId, "edge secondSourceId"),
      supersededClaimId: optStr(o.supersededClaimId, "edge supersededClaimId"),
      supersedingClaimId: optStr(o.supersedingClaimId, "edge supersedingClaimId"),
    };
    if (!known.has(edge.firstClaimId) || !known.has(edge.secondClaimId)) throw new MalformedGraphResponse("edge references unknown claim");
    return edge;
  });
  return { nodes, edges };
}

const REASON_TEXT: Record<string, string> = {
  entity_unresolved: "At least one claim has no resolved entity, so the pair cannot be compared safely.",
  same_value_same_polarity: "Both claims assert the same value.",
  polarity_conflict: "One claim asserts what the other denies.",
  value_conflict_same_as_of: "Different values for the same date.",
  later_as_of_supersedes: "The later as-of date supersedes the earlier claim.",
  as_of_missing: "A date is missing, so conflict cannot be told apart from supersession.",
  both_negated_different_value: "Both claims deny different values.",
  opposite_polarity_different_value: "Opposite polarity with different values.",
};
export const describeReason = (code: string): string => REASON_TEXT[code] ?? `Reason code: ${code}`;

export type GraphFailureKind = "invalid" | "unauthenticated" | "forbidden" | "bad-request" | "offline" | "server" | "malformed";
export type GraphFailure = { kind: GraphFailureKind; message: string };

export function classifyGraphFailure(error: unknown, online: boolean = true): GraphFailure {
  if (error instanceof InvalidGraphInput) return { kind: "invalid", message: error.message };
  if (error instanceof MalformedGraphResponse) return { kind: "malformed", message: error.message };
  const status = typeof error === "object" && error !== null ? (error as { status?: unknown }).status : undefined;
  if (status === 401) return { kind: "unauthenticated", message: "Your session has ended or is not valid. Sign in again." };
  if (status === 403) return { kind: "forbidden", message: "Your account needs an organization and exactly one workflow role to compare claims." };
  if (status === 400) return { kind: "bad-request", message: "The relation service rejected that claim set. Nothing is shown rather than a guess." };
  if (typeof status === "number") return { kind: "server", message: `The relation service failed (HTTP ${status}). Nothing is shown rather than a guess.` };
  return {
    kind: "offline",
    message: online ? "The relation service could not be reached. Check your connection and try again." : "You appear to be offline. Reconnect and try again.",
  };
}

export async function buildRelationGraph(claims: ClaimInput[]): Promise<RelationGraph> {
  const body = buildGraphBody(claims);
  const ids = body.claims.map((c) => c.claimId);
  return parseGraphResponse(await apiPost<unknown, typeof body>(RELATION_GRAPH_PATH, body), ids);
}
