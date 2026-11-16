import { apiPost } from "./http";

// VER-P0-17: client for the statement classifier (VER-P0-11,
// POST /api/v1/tenant-evidence/statement-classification). The server alone reads the verified evidence and
// decides the class; this client never judges or repairs. A classification is not a claim and is never
// answerable. No seeded fallback; a malformed response is rejected whole.

export const CLASSIFY_PATH = "/api/v1/tenant-evidence/statement-classification";
export const MAX_STATEMENT_LENGTH = 2000;
export const MAX_REF_LENGTH = 500;

export const STATEMENT_CLASSES = ["quotation", "inference", "unresolved", "unsupported"] as const;
export type StatementClass = (typeof STATEMENT_CLASSES)[number];

export type ClassifyInput = { evidenceRef: string; statement: string };
export type StatementSpan = { elementId: string; start: number; length: number };
export type ClassificationFreshness = { state: string; eligible: boolean; reason: string | null; evaluatedAt: string | null };
export type ClassificationResult = {
  evidenceRef: string;
  classifierName: string;
  classifierVersion: string;
  statementClass: StatementClass;
  supportingEvidenceRef: string | null;
  span: StatementSpan | null;
  freshness: ClassificationFreshness;
};

export class InvalidClassifyInput extends Error {
  constructor(detail: string) {
    super(detail);
    this.name = "InvalidClassifyInput";
  }
}

export class MalformedClassifyResponse extends Error {
  constructor(detail: string) {
    super(`The classification service returned an unexpected response (${detail}).`);
    this.name = "MalformedClassifyResponse";
  }
}

const hasControl = (s: string) => Array.from(s).some((ch) => ch.charCodeAt(0) < 32 || ch.charCodeAt(0) === 127);

export function buildClassifyBody(input: ClassifyInput) {
  const evidenceRef = input.evidenceRef.trim();
  if (evidenceRef === "" || evidenceRef.length > MAX_REF_LENGTH || hasControl(evidenceRef)) {
    throw new InvalidClassifyInput(`Enter an evidence reference of 1-${MAX_REF_LENGTH} characters.`);
  }
  const statement = input.statement.trim();
  if (statement === "" || statement.length > MAX_STATEMENT_LENGTH || hasControl(statement)) {
    throw new InvalidClassifyInput(`Enter a statement of 1-${MAX_STATEMENT_LENGTH} characters with no control characters.`);
  }
  return { evidenceRef, statement };
}

const str = (v: unknown, name: string): string => {
  if (typeof v !== "string") throw new MalformedClassifyResponse(name);
  return v;
};
const nullableStr = (v: unknown, name: string): string | null => (v === null || v === undefined ? null : str(v, name));
const int = (v: unknown, name: string): number => {
  if (typeof v !== "number" || !Number.isInteger(v) || v < 0) throw new MalformedClassifyResponse(name);
  return v;
};

export function parseClassifyResponse(raw: unknown, expectedRef: string): ClassificationResult {
  if (typeof raw !== "object" || raw === null) throw new MalformedClassifyResponse("not an object");
  const r = raw as Record<string, unknown>;
  if (r.evidenceRef !== expectedRef) throw new MalformedClassifyResponse("evidenceRef");
  // A classification is never a claim: anything but an explicit false is refused.
  if (r.answerable !== false) throw new MalformedClassifyResponse("answerable");
  const statementClass = r.class;
  if (!(STATEMENT_CLASSES as readonly unknown[]).includes(statementClass)) throw new MalformedClassifyResponse("class");
  const f = r.freshness as Record<string, unknown> | null | undefined;
  if (typeof f !== "object" || f === null || typeof f.eligible !== "boolean") throw new MalformedClassifyResponse("freshness");
  const freshness: ClassificationFreshness = {
    state: str(f.state, "freshness state"),
    eligible: f.eligible,
    reason: nullableStr(f.reason, "freshness reason"),
    evaluatedAt: nullableStr(f.evaluatedAt, "freshness evaluatedAt"),
  };
  let span: StatementSpan | null = null;
  if (r.span !== null && r.span !== undefined) {
    const s = r.span as Record<string, unknown>;
    if (typeof s !== "object") throw new MalformedClassifyResponse("span");
    span = { elementId: str(s.elementId, "span elementId"), start: int(s.start, "span start"), length: int(s.length, "span length") };
  }
  // A quotation without its exact span would be an unverifiable quote.
  if (statementClass === "quotation" && span === null) throw new MalformedClassifyResponse("quotation without span");
  return {
    evidenceRef: expectedRef,
    classifierName: str(r.classifierName, "classifierName"),
    classifierVersion: str(r.classifierVersion, "classifierVersion"),
    statementClass: statementClass as StatementClass,
    supportingEvidenceRef: nullableStr(r.supportingEvidenceRef, "supportingEvidenceRef"),
    span,
    freshness,
  };
}

export type ClassifyFailureKind = "invalid" | "unauthenticated" | "forbidden" | "not-found" | "refused" | "bad-request" | "offline" | "server" | "malformed";
export type ClassifyFailure = { kind: ClassifyFailureKind; message: string };

// Fixed messages only: neither the statement nor server text is echoed.
export function classifyClassificationFailure(error: unknown, online: boolean = true): ClassifyFailure {
  if (error instanceof InvalidClassifyInput) return { kind: "invalid", message: error.message };
  if (error instanceof MalformedClassifyResponse) return { kind: "malformed", message: error.message };
  const status = typeof error === "object" && error !== null ? (error as { status?: unknown }).status : undefined;
  if (status === 401) return { kind: "unauthenticated", message: "Your session has ended or is not valid. Sign in again." };
  if (status === 403) return { kind: "forbidden", message: "Your account needs an organization and exactly one workflow role to classify a statement." };
  if (status === 404) return { kind: "not-found", message: "No verified evidence with that reference exists in your organization." };
  if (status === 409) return { kind: "refused", message: "The stored evidence failed integrity checks, so no classification is shown." };
  if (status === 400) return { kind: "bad-request", message: "The classification service rejected that request. Nothing is shown rather than a guess." };
  if (typeof status === "number") {
    return { kind: "server", message: `The classification service failed (HTTP ${status}). Nothing is shown rather than a guess.` };
  }
  return {
    kind: "offline",
    message: online ? "The classification service could not be reached. Check your connection and try again." : "You appear to be offline. Reconnect and try again.",
  };
}

export async function classifyStatement(input: ClassifyInput): Promise<ClassificationResult> {
  const body = buildClassifyBody(input);
  return parseClassifyResponse(await apiPost<unknown, typeof body>(CLASSIFY_PATH, body), body.evidenceRef);
}
