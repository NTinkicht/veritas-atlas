import { apiPost } from "./http";
import { toFreshnessLabel, type FreshnessLabel } from "./evidenceSearch";

// VER-P0-17: client for the claim-to-evidence validator (VER-P0-12,
// POST /api/v1/tenant-claims/validate-evidence). The server alone decides validity; this client never
// judges, repairs or substitutes evidence. No seeded fallback; a malformed response is rejected whole.

export const VALIDATE_PATH = "/api/v1/tenant-claims/validate-evidence";
export const MAX_CITATIONS = 20;
export const MAX_TEXT_LENGTH = 128;
const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

export type CitationRow = { evidenceRef: string; parserName: string; parserVersion: string; scopeSourceEntryId: string };
export type CitationPolicy = { evidenceRequired: boolean; historicalEvidenceAllowed: boolean; historicalTimeContextPreserved: boolean };

export type CitationItem = {
  evidenceRef: string;
  valid: boolean;
  code: string | null;
  freshness: FreshnessLabel | null;
  evaluatedAt: string | null;
  historicalAsOf: string | null;
};
export type CitationVerdict = { valid: boolean; code: string | null; items: CitationItem[] };

export class InvalidCitationInput extends Error {
  constructor(detail: string) {
    super(detail);
    this.name = "InvalidCitationInput";
  }
}

export class MalformedCitationResponse extends Error {
  constructor(detail: string) {
    super(`The citation service returned an unexpected response (${detail}).`);
    this.name = "MalformedCitationResponse";
  }
}

const hasControl = (s: string) => Array.from(s).some((ch) => ch.charCodeAt(0) < 32 || ch.charCodeAt(0) === 127);

function optionalText(label: string, raw: string): string | null {
  const v = raw.trim();
  if (v === "") return null;
  if (v.length > MAX_TEXT_LENGTH || hasControl(v)) throw new InvalidCitationInput(`${label} must be at most ${MAX_TEXT_LENGTH} characters with no control characters.`);
  return v;
}

export function buildValidationBody(policy: CitationPolicy, rows: CitationRow[]) {
  if (rows.length > MAX_CITATIONS) throw new InvalidCitationInput(`Cite at most ${MAX_CITATIONS} evidence items.`);
  const citations = rows.map((row, i) => {
    const n = i + 1;
    const evidenceRef = optionalText(`Citation ${n} reference`, row.evidenceRef);
    if (evidenceRef === null) throw new InvalidCitationInput(`Citation ${n} needs an evidence reference.`);
    const scope = optionalText(`Citation ${n} scope source entry`, row.scopeSourceEntryId);
    if (scope !== null && !GUID.test(scope)) throw new InvalidCitationInput(`Citation ${n} scope source entry must be a GUID, or left blank.`);
    return {
      evidenceRef,
      parserName: optionalText(`Citation ${n} parser name`, row.parserName),
      parserVersion: optionalText(`Citation ${n} parser version`, row.parserVersion),
      scopeSourceEntryId: scope,
    };
  });
  return {
    evidenceRequired: policy.evidenceRequired === true,
    historicalEvidenceAllowed: policy.historicalEvidenceAllowed === true,
    historicalTimeContextPreserved: policy.historicalTimeContextPreserved === true,
    citations,
  };
}

const nullableText = (v: unknown, field: string): string | null => {
  if (v === null || v === undefined) return null;
  if (typeof v !== "string") throw new MalformedCitationResponse(field);
  return v;
};

// `expectedValid` comes from the HTTP status (200 -> true, 422 -> false); a body that disagrees with it,
// or whose flags contradict its own items, is rejected whole.
export function parseValidationResponse(raw: unknown, expectedValid: boolean): CitationVerdict {
  if (typeof raw !== "object" || raw === null) throw new MalformedCitationResponse("not an object");
  const r = raw as Record<string, unknown>;
  if (r.valid !== expectedValid) throw new MalformedCitationResponse("valid");
  const code = nullableText(r.code, "code");
  if (expectedValid ? code !== null : code === null || code === "") throw new MalformedCitationResponse("code");
  if (!Array.isArray(r.items)) throw new MalformedCitationResponse("items");
  const items = r.items.map((entry): CitationItem => {
    if (typeof entry !== "object" || entry === null) throw new MalformedCitationResponse("item");
    const e = entry as Record<string, unknown>;
    if (typeof e.evidenceRef !== "string" || typeof e.valid !== "boolean") throw new MalformedCitationResponse("item fields");
    const itemCode = nullableText(e.code, "item code");
    if (e.valid ? itemCode !== null : itemCode === null || itemCode === "") throw new MalformedCitationResponse("item code");
    return {
      evidenceRef: e.evidenceRef,
      valid: e.valid,
      code: itemCode,
      freshness: e.freshness === null || e.freshness === undefined ? null : toFreshnessLabel(e.freshness),
      evaluatedAt: nullableText(e.evaluatedAt, "evaluatedAt"),
      historicalAsOf: nullableText(e.historicalAsOf, "historicalAsOf"),
    };
  });
  if (expectedValid && items.some((i) => !i.valid)) throw new MalformedCitationResponse("valid with an invalid item");
  return { valid: expectedValid, code, items };
}

const CODE_TEXT: Record<string, string> = {
  EVIDENCE_NOT_FOUND: "The cited evidence was not found in your organization.",
  EVIDENCE_DIGEST_MISMATCH: "The cited evidence does not match its recorded content digest.",
  EVIDENCE_REFERENCE_PARSER_MISMATCH: "The reference does not match the stated parser name and version.",
  EVIDENCE_REQUIRED: "This claim requires evidence but none was cited.",
  PROVENANCE_INCOMPLETE: "The cited evidence has incomplete provenance.",
  EVIDENCE_SCOPE_MISMATCH: "The cited evidence is outside the permitted source scope.",
  EVIDENCE_STALE: "The cited evidence is stale for a current-state claim.",
  EVIDENCE_FRESHNESS_UNKNOWN: "The freshness of the cited evidence is unknown.",
};

export function describeCitationCode(code: string): string {
  return CODE_TEXT[code] ?? `Rejection code: ${code}`;
}

export type CitationFailureKind = "invalid" | "unauthenticated" | "forbidden" | "bad-request" | "offline" | "server" | "malformed";
export type CitationFailure = { kind: CitationFailureKind; message: string };

export function classifyCitationFailure(error: unknown, online: boolean = true): CitationFailure {
  if (error instanceof InvalidCitationInput) return { kind: "invalid", message: error.message };
  if (error instanceof MalformedCitationResponse) return { kind: "malformed", message: error.message };
  const status = typeof error === "object" && error !== null ? (error as { status?: unknown }).status : undefined;
  if (status === 401) return { kind: "unauthenticated", message: "Your session has ended or is not valid. Sign in again." };
  if (status === 403) return { kind: "forbidden", message: "Your account needs an organization to inspect citations." };
  if (status === 400) return { kind: "bad-request", message: "The citation service rejected that request. Nothing is shown rather than a guess." };
  if (typeof status === "number") {
    return { kind: "server", message: `The citation service failed (HTTP ${status}). Nothing is shown rather than a guess.` };
  }
  return {
    kind: "offline",
    message: online ? "The citation service could not be reached. Check your connection and try again." : "You appear to be offline. Reconnect and try again.",
  };
}

export async function inspectCitations(policy: CitationPolicy, rows: CitationRow[]): Promise<CitationVerdict> {
  const body = buildValidationBody(policy, rows);
  try {
    return parseValidationResponse(await apiPost<unknown, typeof body>(VALIDATE_PATH, body), true);
  } catch (error) {
    // 422 is a normal "rejected" verdict whose body carries the stable codes; any other failure propagates.
    if (typeof error === "object" && error !== null && (error as { status?: unknown }).status === 422) {
      return parseValidationResponse((error as { raw?: unknown }).raw, false);
    }
    throw error;
  }
}
