import { apiPost } from "./http";

// VER-P0-17: client for the stateless claim confidence labeler (VER-P0-14,
// POST /api/v1/tenant-claims/confidence). The band is quality-of-evidence only, never truth.
// Blank counts stay omitted so the server reports "insufficient" rather than a default of 0.
// No seeded fallback; a malformed response is rejected whole (fail closed).

export const CONFIDENCE_PATH = "/api/v1/tenant-claims/confidence";
export const MAX_COUNT = 100_000;
export const MAX_CLAIM_ID_LENGTH = 500;

export const COUNT_FIELDS = [
  "supportingEvidenceCount",
  "independentSourceCount",
  "conflictingCount",
  "supersededCount",
  "staleCount",
  "unknownFreshnessCount",
  "unresolvedRelationCount",
] as const;
export type CountField = (typeof COUNT_FIELDS)[number];

export const BANDS = ["insufficient", "low", "medium", "high"] as const;
export type ConfidenceBandName = (typeof BANDS)[number];

export type ClaimConfidenceLabel = { claimId: string; band: ConfidenceBandName; reasons: string[] };

export class InvalidConfidenceInput extends Error {
  constructor(detail: string) {
    super(detail);
    this.name = "InvalidConfidenceInput";
  }
}

export class MalformedConfidenceResponse extends Error {
  constructor(detail: string) {
    super(`The confidence service returned an unexpected response (${detail}).`);
    this.name = "MalformedConfidenceResponse";
  }
}

export function buildConfidenceBody(claimId: string, counts: Partial<Record<CountField, string>>) {
  const id = claimId.trim();
  if (id.length === 0 || id.length > MAX_CLAIM_ID_LENGTH || Array.from(id).some((ch) => ch.charCodeAt(0) < 32 || ch.charCodeAt(0) === 127)) {
    throw new InvalidConfidenceInput(`Enter a claim id of 1-${MAX_CLAIM_ID_LENGTH} characters.`);
  }
  const claim: Record<string, unknown> = { claimId: id };
  for (const field of COUNT_FIELDS) {
    const raw = (counts[field] ?? "").trim();
    if (raw === "") continue;
    if (!/^\d+$/.test(raw) || Number(raw) > MAX_COUNT) {
      throw new InvalidConfidenceInput(`${field} must be a whole number from 0 to ${MAX_COUNT}, or left blank.`);
    }
    claim[field] = Number(raw);
  }
  return { claims: [claim] };
}

export function parseConfidenceResponse(raw: unknown, expectedClaimId: string): ClaimConfidenceLabel {
  if (typeof raw !== "object" || raw === null) throw new MalformedConfidenceResponse("not an object");
  const r = raw as Record<string, unknown>;
  if (r.evidenceVerified !== false) throw new MalformedConfidenceResponse("evidenceVerified");
  if (!Array.isArray(r.labels) || r.labels.length !== 1) throw new MalformedConfidenceResponse("labels");
  const label = r.labels[0] as Record<string, unknown> | null;
  if (typeof label !== "object" || label === null) throw new MalformedConfidenceResponse("label");
  if (label.claimId !== expectedClaimId) throw new MalformedConfidenceResponse("claimId");
  const band = label.band;
  if (typeof band !== "string" || !(BANDS as readonly string[]).includes(band)) throw new MalformedConfidenceResponse("band");
  const reasons = label.reasons;
  if (!Array.isArray(reasons) || reasons.some((x) => typeof x !== "string" || x.length === 0)) {
    throw new MalformedConfidenceResponse("reasons");
  }
  return { claimId: expectedClaimId, band: band as ConfidenceBandName, reasons: reasons as string[] };
}

const REASON_TEXT: Record<string, string> = {
  input_missing: "A required quality signal was not supplied.",
  no_support: "No supporting evidence was counted.",
  conflicting_evidence: "Conflicting evidence exists.",
  superseded_evidence: "Some evidence has been superseded.",
  stale_evidence: "Some evidence is stale.",
  unknown_freshness: "Freshness of some evidence is unknown.",
  unresolved_relations: "Some claim relations are unresolved.",
  single_independent_source: "Only one independent source supports the claim.",
  multiple_independent_sources: "Several independent sources support the claim.",
};

export function describeReason(code: string): string {
  return REASON_TEXT[code] ?? `Reason code: ${code}`;
}

export type ConfidenceFailureKind = "invalid" | "unauthenticated" | "forbidden" | "bad-request" | "offline" | "server" | "malformed";
export type ConfidenceFailure = { kind: ConfidenceFailureKind; message: string };

export function classifyConfidenceFailure(error: unknown, online: boolean = true): ConfidenceFailure {
  if (error instanceof InvalidConfidenceInput) return { kind: "invalid", message: error.message };
  if (error instanceof MalformedConfidenceResponse) return { kind: "malformed", message: error.message };
  const status = typeof error === "object" && error !== null ? (error as { status?: unknown }).status : undefined;
  if (status === 401) return { kind: "unauthenticated", message: "Your session has ended or is not valid. Sign in again." };
  if (status === 403) return { kind: "forbidden", message: "Your account needs an organization and exactly one workflow role to label confidence." };
  if (status === 400) return { kind: "bad-request", message: "The confidence service rejected those values. Nothing is shown rather than a guess." };
  if (typeof status === "number") {
    return { kind: "server", message: `The confidence service failed (HTTP ${status}). Nothing is shown rather than a guess.` };
  }
  return {
    kind: "offline",
    message: online ? "The confidence service could not be reached. Check your connection and try again." : "You appear to be offline. Reconnect and try again.",
  };
}

export async function labelClaimConfidence(claimId: string, counts: Partial<Record<CountField, string>>): Promise<ClaimConfidenceLabel> {
  const body = buildConfidenceBody(claimId, counts);
  const id = body.claims[0].claimId as string;
  return parseConfidenceResponse(await apiPost<unknown, typeof body>(CONFIDENCE_PATH, body), id);
}
