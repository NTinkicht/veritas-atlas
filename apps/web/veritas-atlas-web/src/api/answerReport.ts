import { apiPost } from "./http";

// VER-P0-17: client for the source-backed report composer (VER-P0-15,
// POST /api/v1/tenant-answers/compose). The server alone resolves evidence, freshness and the serve decision;
// this client never judges, repairs or substitutes anything. One claim per request. A blocked answer carries
// only stable blocker codes and no claim text. No seeded fallback; a malformed response is rejected whole.

export const COMPOSE_PATH = "/api/v1/tenant-answers/compose";
export const MAX_CITATIONS = 20;
export const MAX_CLAIM_TEXT_LENGTH = 2000;
export const MAX_ID_LENGTH = 500;
const MAX_FIELD_LENGTH = 128;
const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const ISO_DATE = /^\d{4}-\d{2}-\d{2}$/;

export const VERIFICATION_STATES = ["unreviewed", "verified", "rejected"] as const;
export type VerificationState = (typeof VERIFICATION_STATES)[number];

export type ComposeInput = {
  claimId: string;
  text: string;
  currentState: boolean;
  historicalAsOf: string;
  citationLines: string;
  humanVerification: VerificationState;
};

export type ReportCitation = { evidenceRef: string; freshness: string };
export type ReportClaim = {
  claimId: string;
  text: string;
  temporalScope: string;
  historicalAsOf: string | null;
  nonCurrentEvidence: boolean;
  citations: ReportCitation[];
};
export type Blocker = { code: string; claimId: string | null; detail: string | null };
export type ComposeResult =
  | { decision: "serve"; humanVerification: string; claim: ReportClaim }
  | { decision: "block"; blockers: Blocker[] };

export class InvalidComposeInput extends Error {
  constructor(detail: string) {
    super(detail);
    this.name = "InvalidComposeInput";
  }
}

export class MalformedComposeResponse extends Error {
  constructor(detail: string) {
    super(`The report service returned an unexpected response (${detail}).`);
    this.name = "MalformedComposeResponse";
  }
}

const hasControl = (s: string, allowLineBreaks = false) =>
  Array.from(s).some((ch) => {
    const c = ch.charCodeAt(0);
    return (c < 32 && !(allowLineBreaks && (c === 9 || c === 10 || c === 13))) || c === 127;
  });

function field(label: string, raw: string, optional = false): string {
  const v = raw.trim();
  if (v === "" && optional) return "";
  if (v === "" || v.length > MAX_FIELD_LENGTH || hasControl(v)) {
    throw new InvalidComposeInput(`${label} must be 1-${MAX_FIELD_LENGTH} characters with no control characters.`);
  }
  return v;
}

// One citation per line: "evidenceRef | parserName | parserVersion | scopeSourceEntryId (optional GUID)".
export function buildComposeBody(input: ComposeInput) {
  const claimId = input.claimId.trim();
  if (claimId === "" || claimId.length > MAX_ID_LENGTH || hasControl(claimId)) {
    throw new InvalidComposeInput(`Enter a claim id of 1-${MAX_ID_LENGTH} characters.`);
  }
  const text = input.text.trim();
  if (text === "" || text.length > MAX_CLAIM_TEXT_LENGTH || hasControl(text, true)) {
    throw new InvalidComposeInput(`Enter the claim text (1-${MAX_CLAIM_TEXT_LENGTH} characters).`);
  }
  const asOf = input.historicalAsOf.trim();
  if (asOf !== "" && !ISO_DATE.test(asOf)) throw new InvalidComposeInput("The historical as-of date must be YYYY-MM-DD, or left blank.");
  if (input.currentState && asOf !== "") throw new InvalidComposeInput("A current-state claim has no historical as-of date.");
  if (!(VERIFICATION_STATES as readonly string[]).includes(input.humanVerification)) {
    throw new InvalidComposeInput("Choose a human verification state.");
  }
  const lines = input.citationLines.split("\n").map((l) => l.trim()).filter((l) => l !== "");
  if (lines.length === 0) throw new InvalidComposeInput("Cite at least one evidence item.");
  if (lines.length > MAX_CITATIONS) throw new InvalidComposeInput(`Cite at most ${MAX_CITATIONS} evidence items.`);
  const citations = lines.map((line, i) => {
    const parts = line.split("|");
    const n = i + 1;
    if (parts.length < 3 || parts.length > 4) {
      throw new InvalidComposeInput(`Citation ${n} must be: reference | parser name | parser version | optional scope GUID.`);
    }
    const scope = field(`Citation ${n} scope source entry`, parts[3] ?? "", true);
    if (scope !== "" && !GUID.test(scope)) throw new InvalidComposeInput(`Citation ${n} scope source entry must be a GUID, or left blank.`);
    return {
      evidenceRef: field(`Citation ${n} reference`, parts[0]),
      parserName: field(`Citation ${n} parser name`, parts[1]),
      parserVersion: field(`Citation ${n} parser version`, parts[2]),
      scopeSourceEntryId: scope === "" ? null : scope,
    };
  });
  return {
    claims: [
      { claimId, text, currentState: input.currentState, historicalAsOf: input.currentState || asOf === "" ? null : asOf, citations },
    ],
    contradictions: [],
    disclosedContradictionIds: [],
    humanVerification: input.humanVerification,
  };
}

const str = (v: unknown, name: string): string => {
  if (typeof v !== "string") throw new MalformedComposeResponse(name);
  return v;
};
const nullableStr = (v: unknown, name: string): string | null => (v === null || v === undefined ? null : str(v, name));

export function parseComposeResponse(raw: unknown, expectedClaimId: string): ComposeResult {
  if (typeof raw !== "object" || raw === null) throw new MalformedComposeResponse("not an object");
  const r = raw as Record<string, unknown>;
  if (!Array.isArray(r.blockers)) throw new MalformedComposeResponse("blockers");
  if (r.decision === "block") {
    // A blocked answer must expose reasons and no report content.
    if (r.report !== null && r.report !== undefined) throw new MalformedComposeResponse("report on a blocked answer");
    if (r.blockers.length === 0) throw new MalformedComposeResponse("blockers");
    const blockers = r.blockers.map((b): Blocker => {
      if (typeof b !== "object" || b === null) throw new MalformedComposeResponse("blocker");
      const x = b as Record<string, unknown>;
      return { code: str(x.code, "blocker code"), claimId: nullableStr(x.claimId, "blocker claimId"), detail: nullableStr(x.detail, "blocker detail") };
    });
    return { decision: "block", blockers };
  }
  if (r.decision !== "serve") throw new MalformedComposeResponse("decision");
  if (r.blockers.length !== 0) throw new MalformedComposeResponse("serve with blockers");
  const report = r.report as Record<string, unknown> | null | undefined;
  if (typeof report !== "object" || report === null || !Array.isArray(report.claims) || report.claims.length !== 1) {
    throw new MalformedComposeResponse("report");
  }
  const c = report.claims[0] as Record<string, unknown> | null;
  if (typeof c !== "object" || c === null || c.claimId !== expectedClaimId) throw new MalformedComposeResponse("claimId");
  if (typeof c.nonCurrentEvidence !== "boolean" || !Array.isArray(c.citations) || c.citations.length === 0) {
    throw new MalformedComposeResponse("claim fields");
  }
  const citations = c.citations.map((x): ReportCitation => {
    const e = (typeof x === "object" && x !== null ? x : {}) as Record<string, unknown>;
    return { evidenceRef: str(e.evidenceRef, "citation ref"), freshness: str(e.freshness, "citation freshness") };
  });
  const historicalAsOf = nullableStr(c.historicalAsOf, "historicalAsOf");
  // Non-current evidence must carry its as-of context; never show it as a bare assertion.
  if (c.nonCurrentEvidence && historicalAsOf === null) throw new MalformedComposeResponse("non-current evidence without as-of date");
  return {
    decision: "serve",
    humanVerification: str(report.humanVerification, "humanVerification"),
    claim: {
      claimId: expectedClaimId,
      text: str(c.text, "text"),
      temporalScope: str(c.temporalScope, "temporalScope"),
      historicalAsOf,
      nonCurrentEvidence: c.nonCurrentEvidence,
      citations,
    },
  };
}

const BLOCKER_TEXT: Record<string, string> = {
  tenant_missing: "Your account has no organization.",
  no_claims: "No claim was supplied.",
  claim_without_citation: "The claim cites no evidence.",
  citation_blank_ref: "A citation has no evidence reference.",
  citation_duplicate_ref: "The same evidence is cited twice for one claim.",
  citation_not_verified: "A citation is not verified, answerable evidence in your organization.",
  citation_cross_tenant: "A citation belongs to another organization.",
  current_state_evidence_not_current: "A current-state claim cites evidence that is not current.",
  historical_as_of_missing: "Non-current evidence needs a historical as-of date.",
  contradiction_not_disclosed: "A known contradiction is not disclosed.",
  human_verification_missing: "A human has not verified this answer.",
  claim_text_missing: "The claim has no text.",
};

export function describeBlocker(code: string): string {
  return BLOCKER_TEXT[code] ?? `Blocker code: ${code}`;
}

export type ComposeFailureKind = "invalid" | "unauthenticated" | "forbidden" | "bad-request" | "offline" | "server" | "malformed";
export type ComposeFailure = { kind: ComposeFailureKind; message: string };

export function classifyComposeFailure(error: unknown, online: boolean = true): ComposeFailure {
  if (error instanceof InvalidComposeInput) return { kind: "invalid", message: error.message };
  if (error instanceof MalformedComposeResponse) return { kind: "malformed", message: error.message };
  const status = typeof error === "object" && error !== null ? (error as { status?: unknown }).status : undefined;
  if (status === 401) return { kind: "unauthenticated", message: "Your session has ended or is not valid. Sign in again." };
  if (status === 403) return { kind: "forbidden", message: "Your account needs an organization and exactly one workflow role to compose a report." };
  if (status === 400) return { kind: "bad-request", message: "The report service rejected that request. Nothing is shown rather than a guess." };
  if (typeof status === "number") {
    return { kind: "server", message: `The report service failed (HTTP ${status}). Nothing is shown rather than a guess.` };
  }
  return {
    kind: "offline",
    message: online ? "The report service could not be reached. Check your connection and try again." : "You appear to be offline. Reconnect and try again.",
  };
}

export async function composeReport(input: ComposeInput): Promise<ComposeResult> {
  const body = buildComposeBody(input);
  return parseComposeResponse(await apiPost<unknown, typeof body>(COMPOSE_PATH, body), body.claims[0].claimId);
}
