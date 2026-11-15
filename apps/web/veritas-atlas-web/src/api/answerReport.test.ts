import { describe, expect, it } from "vitest";
import {
  InvalidComposeInput,
  MalformedComposeResponse,
  buildComposeBody,
  classifyComposeFailure,
  describeBlocker,
  parseComposeResponse,
  type ComposeInput,
} from "./answerReport";

// SYNTHETIC fixtures only.
const input: ComposeInput = {
  claimId: " c-1 ",
  text: " The register lists X. ",
  currentState: true,
  historicalAsOf: "",
  citationLines: "ev1:abc | parser-a | 1.0.0\n\nev1:def | parser-a | 1.0.0 | 11111111-2222-3333-4444-555555555555",
  humanVerification: "verified",
};
const claim = { claimId: "c-1", text: "T", temporalScope: "current", historicalAsOf: null, nonCurrentEvidence: false, citations: [{ evidenceRef: "ev1:abc", freshness: "current" }] };
const serve = { decision: "serve", humanVerificationSource: "caller_asserted", blockers: [], report: { humanVerification: "Verified", claims: [claim], contradictions: [] } };
const block = { decision: "block", humanVerificationSource: "caller_asserted", blockers: [{ code: "citation_not_verified", claimId: "c-1", detail: null }], report: null };

describe("compose request body", () => {
  it("builds one claim with trimmed fields and optional scope", () => {
    const body = buildComposeBody(input);
    expect(body.humanVerification).toBe("verified");
    expect(body.claims[0]).toMatchObject({ claimId: "c-1", text: "The register lists X.", currentState: true, historicalAsOf: null });
    expect(body.claims[0].citations).toEqual([
      { evidenceRef: "ev1:abc", parserName: "parser-a", parserVersion: "1.0.0", scopeSourceEntryId: null },
      { evidenceRef: "ev1:def", parserName: "parser-a", parserVersion: "1.0.0", scopeSourceEntryId: "11111111-2222-3333-4444-555555555555" },
    ]);
  });
  it("sends the as-of date only for a non-current claim", () => {
    expect(buildComposeBody({ ...input, currentState: false, historicalAsOf: "2020-01-31" }).claims[0].historicalAsOf).toBe("2020-01-31");
    expect(() => buildComposeBody({ ...input, currentState: true, historicalAsOf: "2020-01-31" })).toThrow(InvalidComposeInput);
    expect(() => buildComposeBody({ ...input, currentState: false, historicalAsOf: "31/01/2020" })).toThrow(InvalidComposeInput);
  });
  it("refuses bad input before any request", () => {
    expect(() => buildComposeBody(input)).not.toThrow();
    for (const bad of [
      { claimId: " " }, { claimId: "a\nb" }, { text: "  " }, { text: "x".repeat(2001) }, { text: "a\u0000b" },
      { citationLines: "" }, { citationLines: "ev1:a | p" }, { citationLines: "ev1:a | p | 1 | not-a-guid" },
      { citationLines: "ev1:a | | 1" }, { citationLines: Array.from({ length: 21 }, (_, i) => `r${i} | p | 1`).join("\n") },
      { humanVerification: "approved" as never },
    ]) {
      expect(() => buildComposeBody({ ...input, ...bad })).toThrow(InvalidComposeInput);
    }
  });
});

describe("compose response", () => {
  it("accepts a served report", () => {
    const r = parseComposeResponse(serve, "c-1");
    expect(r).toMatchObject({ decision: "serve", humanVerification: "Verified" });
  });
  it("accepts a blocked answer with codes only", () => {
    expect(parseComposeResponse(block, "c-1")).toEqual({ decision: "block", blockers: [{ code: "citation_not_verified", claimId: "c-1", detail: null }] });
  });
  it("rejects contradictory or malformed bodies whole", () => {
    const bad: unknown[] = [
      null, "x", {}, { ...serve, decision: "maybe" },
      { ...block, report: serve.report }, { ...block, blockers: [] },
      { ...serve, blockers: block.blockers }, { ...serve, report: null },
      { ...serve, report: { ...serve.report, claims: [{ ...claim, claimId: "other" }] } },
      { ...serve, report: { ...serve.report, claims: [{ ...claim, citations: [] }] } },
      { ...serve, report: { ...serve.report, claims: [{ ...claim, nonCurrentEvidence: true, historicalAsOf: null }] } },
      { ...serve, report: { ...serve.report, claims: [{ ...claim, text: 5 }] } },
    ];
    for (const b of bad) expect(() => parseComposeResponse(b, "c-1")).toThrow(MalformedComposeResponse);
  });
  it("keeps historical disclosure context", () => {
    const r = parseComposeResponse({ ...serve, report: { ...serve.report, claims: [{ ...claim, nonCurrentEvidence: true, historicalAsOf: "2020-01-31" }] } }, "c-1");
    expect(r.decision === "serve" && r.claim.historicalAsOf).toBe("2020-01-31");
  });
});

describe("messages", () => {
  it("describes known and unknown blocker codes", () => {
    expect(describeBlocker("claim_without_citation")).toMatch(/cites no evidence/);
    expect(describeBlocker("zzz")).toBe("Blocker code: zzz");
  });
  it("classifies failures", () => {
    expect(classifyComposeFailure(new InvalidComposeInput("x")).kind).toBe("invalid");
    expect(classifyComposeFailure({ status: 401 }).kind).toBe("unauthenticated");
    expect(classifyComposeFailure({ status: 403 }).kind).toBe("forbidden");
    expect(classifyComposeFailure({ status: 400 }).kind).toBe("bad-request");
    expect(classifyComposeFailure({ status: 500 }).kind).toBe("server");
    expect(classifyComposeFailure(new TypeError("net"), false).message).toMatch(/offline/);
  });
});
