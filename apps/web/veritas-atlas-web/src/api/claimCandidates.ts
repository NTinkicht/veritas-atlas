import { apiGet } from "./http";

// VER-P0-17: client for claim candidates (VER-P0-11, GET /api/v1/tenant-evidence/claim-candidates).
// The server alone reads the verified evidence and extracts candidates; this client never extracts, judges or
// repairs. A candidate is an exact quotation with its chunk span: always unconfirmed and never answerable.
// No seeded fallback; a malformed response is rejected whole.

export const CLAIM_CANDIDATES_PATH = "/api/v1/tenant-evidence/claim-candidates";
export const MAX_REF_LENGTH = 512;
export const MAX_CANDIDATES = 500;

export type CandidateFreshness = { state: string; eligible: boolean; reason: string | null; evaluatedAt: string | null };
export type ClaimCandidate = {
  claimKey: string;
  quotation: string;
  elementId: string;
  start: number;
  length: number;
  chunkTextSha256: string;
  status: "unconfirmed";
};
export type ClaimCandidatesResult = {
  evidenceRef: string;
  extractorName: string;
  extractorVersion: string;
  freshness: CandidateFreshness;
  claims: ClaimCandidate[];
};

export class InvalidCandidatesInput extends Error {
  constructor(detail: string) {
    super(detail);
    this.name = "InvalidCandidatesInput";
  }
}

export class MalformedCandidatesResponse extends Error {
  constructor(detail: string) {
    super(`The claim candidate service returned an unexpected response (${detail}).`);
    this.name = "MalformedCandidatesResponse";
  }
}

const hasControl = (s: string) => Array.from(s).some((ch) => ch.charCodeAt(0) < 32 || ch.charCodeAt(0) === 127);

export function buildCandidatesUrl(evidenceRef: string): string {
  const ref = evidenceRef.trim();
  if (ref === "" || ref.length > MAX_REF_LENGTH || hasControl(ref)) {
    throw new InvalidCandidatesInput(`Enter an evidence reference of 1-${MAX_REF_LENGTH} characters.`);
  }
  return `${CLAIM_CANDIDATES_PATH}?evidenceRef=${encodeURIComponent(ref)}`;
}

const str = (v: unknown, name: string): string => {
  if (typeof v !== "string") throw new MalformedCandidatesResponse(name);
  return v;
};
const nullableStr = (v: unknown, name: string): string | null => (v === null || v === undefined ? null : str(v, name));
const int = (v: unknown, name: string): number => {
  if (typeof v !== "number" || !Number.isInteger(v) || v < 0) throw new MalformedCandidatesResponse(name);
  return v;
};

export function parseCandidatesResponse(raw: unknown, expectedRef: string): ClaimCandidatesResult {
  if (typeof raw !== "object" || raw === null) throw new MalformedCandidatesResponse("not an object");
  const r = raw as Record<string, unknown>;
  if (r.evidenceRef !== expectedRef) throw new MalformedCandidatesResponse("evidenceRef");
  const f = r.freshness as Record<string, unknown> | null | undefined;
  if (typeof f !== "object" || f === null || typeof f.eligible !== "boolean") throw new MalformedCandidatesResponse("freshness");
  const freshness: CandidateFreshness = {
    state: str(f.state, "freshness state"),
    eligible: f.eligible,
    reason: nullableStr(f.reason, "freshness reason"),
    evaluatedAt: nullableStr(f.evaluatedAt, "freshness evaluatedAt"),
  };
  if (!Array.isArray(r.claims) || r.claims.length > MAX_CANDIDATES) throw new MalformedCandidatesResponse("claims");
  const claims = r.claims.map((item: unknown): ClaimCandidate => {
    if (typeof item !== "object" || item === null) throw new MalformedCandidatesResponse("claim");
    const c = item as Record<string, unknown>;
    // A candidate is never a claim: anything but an explicit false / "unconfirmed" is refused whole.
    if (c.answerable !== false) throw new MalformedCandidatesResponse("answerable");
    if (c.status !== "unconfirmed") throw new MalformedCandidatesResponse("status");
    const quotation = str(c.quotation, "quotation");
    // A quotation without its exact span would be an unverifiable quote.
    if (quotation === "") throw new MalformedCandidatesResponse("empty quotation");
    const length = int(c.length, "length");
    if (length === 0) throw new MalformedCandidatesResponse("empty span");
    return {
      claimKey: str(c.claimKey, "claimKey"),
      quotation,
      elementId: str(c.elementId, "elementId"),
      start: int(c.start, "start"),
      length,
      chunkTextSha256: str(c.chunkTextSha256, "chunkTextSha256"),
      status: "unconfirmed",
    };
  });
  return {
    evidenceRef: expectedRef,
    extractorName: str(r.extractorName, "extractorName"),
    extractorVersion: str(r.extractorVersion, "extractorVersion"),
    freshness,
    claims,
  };
}

export type CandidatesFailureKind = "invalid" | "unauthenticated" | "forbidden" | "not-found" | "refused" | "bad-request" | "offline" | "server" | "malformed";
export type CandidatesFailure = { kind: CandidatesFailureKind; message: string };

// Fixed messages only: server text is never echoed.
export function classifyCandidatesFailure(error: unknown, online: boolean = true): CandidatesFailure {
  if (error instanceof InvalidCandidatesInput) return { kind: "invalid", message: error.message };
  if (error instanceof MalformedCandidatesResponse) return { kind: "malformed", message: error.message };
  const status = typeof error === "object" && error !== null ? (error as { status?: unknown }).status : undefined;
  if (status === 401) return { kind: "unauthenticated", message: "Your session has ended or is not valid. Sign in again." };
  if (status === 403) return { kind: "forbidden", message: "Your account needs an organization and exactly one workflow role to list claim candidates." };
  if (status === 404) return { kind: "not-found", message: "No verified evidence with that reference exists in your organization." };
  if (status === 409) return { kind: "refused", message: "The stored evidence failed integrity checks, so no claim candidates are shown." };
  if (status === 400) return { kind: "bad-request", message: "The claim candidate service rejected that request. Nothing is shown rather than a guess." };
  if (typeof status === "number") {
    return { kind: "server", message: `The claim candidate service failed (HTTP ${status}). Nothing is shown rather than a guess.` };
  }
  return {
    kind: "offline",
    message: online ? "The claim candidate service could not be reached. Check your connection and try again." : "You appear to be offline. Reconnect and try again.",
  };
}

export async function listClaimCandidates(evidenceRef: string): Promise<ClaimCandidatesResult> {
  const url = buildCandidatesUrl(evidenceRef);
  return parseCandidatesResponse(await apiGet<unknown>(url), evidenceRef.trim());
}
