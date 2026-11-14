import { describe, expect, it } from "vitest";
import {
  InvalidCitationInput,
  MalformedCitationResponse,
  buildValidationBody,
  classifyCitationFailure,
  describeCitationCode,
  parseValidationResponse,
} from "./citationInspector";

// SYNTHETIC fixtures only.
const policy = { evidenceRequired: true, historicalEvidenceAllowed: false, historicalTimeContextPreserved: false };
const row = { evidenceRef: " ev1:abc ", parserName: "", parserVersion: " 1.0 ", scopeSourceEntryId: "" };
const item = { evidenceRef: "ev1:abc", valid: true, code: null, freshness: 0, evaluatedAt: "2026-01-01T00:00:00Z", historicalAsOf: null };
const accepted = { valid: true, code: null, items: [item] };
const rejected = {
  valid: false,
  code: "EVIDENCE_STALE",
  items: [{ ...item, valid: false, code: "EVIDENCE_STALE", freshness: 1 }],
};

describe("validation request body", () => {
  it("trims, sends blank optional fields as null and keeps the policy flags", () => {
    expect(buildValidationBody(policy, [row])).toEqual({
      ...policy,
      citations: [{ evidenceRef: "ev1:abc", parserName: null, parserVersion: "1.0", scopeSourceEntryId: null }],
    });
    expect(buildValidationBody(policy, []).citations).toEqual([]);
  });
  it("refuses bad rows before any request", () => {
    expect(() => buildValidationBody(policy, [{ ...row, evidenceRef: "  " }])).toThrow(InvalidCitationInput);
    expect(() => buildValidationBody(policy, [{ ...row, evidenceRef: "a\nb" }])).toThrow(InvalidCitationInput);
    expect(() => buildValidationBody(policy, [{ ...row, evidenceRef: "x".repeat(129) }])).toThrow(InvalidCitationInput);
    expect(() => buildValidationBody(policy, [{ ...row, scopeSourceEntryId: "not-a-guid" }])).toThrow(InvalidCitationInput);
    expect(() => buildValidationBody(policy, Array.from({ length: 21 }, () => row))).toThrow(InvalidCitationInput);
    expect(buildValidationBody(policy, Array.from({ length: 20 }, () => row)).citations).toHaveLength(20);
    expect(buildValidationBody(policy, [{ ...row, evidenceRef: "x".repeat(128) }]).citations).toHaveLength(1);
    const guid = "123e4567-e89b-12d3-a456-426614174000";
    expect(buildValidationBody(policy, [{ ...row, scopeSourceEntryId: guid }]).citations[0].scopeSourceEntryId).toBe(guid);
  });
});

describe("validation response parsing", () => {
  it("accepts a 200 body and maps the numeric freshness enum, never to CURRENT by default", () => {
    expect(parseValidationResponse(accepted, true).items[0].freshness).toBe("CURRENT");
    expect(parseValidationResponse({ ...accepted, items: [{ ...item, freshness: 99 }] }, true).items[0].freshness).toBe("UNKNOWN");
    expect(parseValidationResponse({ ...accepted, items: [{ ...item, freshness: null }] }, true).items[0].freshness).toBeNull();
  });
  it("accepts a 422 body with the first failure code", () => {
    const v = parseValidationResponse(rejected, false);
    expect(v.valid).toBe(false);
    expect(v.code).toBe("EVIDENCE_STALE");
    expect(v.items[0]).toMatchObject({ valid: false, code: "EVIDENCE_STALE", freshness: "STALE" });
  });
  it("rejects bodies that disagree with the HTTP status or contradict themselves", () => {
    const bad: Array<[unknown, boolean]> = [
      [null, true],
      [rejected, true],
      [accepted, false],
      [{ ...accepted, code: "X" }, true],
      [{ ...rejected, code: null }, false],
      [{ ...rejected, code: "" }, false],
      [{ ...accepted, items: "no" }, true],
      [{ ...accepted, items: [null] }, true],
      [{ ...accepted, items: [{ ...item, valid: "yes" }] }, true],
      [{ ...accepted, items: [{ ...item, valid: false, code: "EVIDENCE_STALE" }] }, true],
      [{ ...accepted, items: [{ ...item, code: "EVIDENCE_STALE" }] }, true],
      [{ ...rejected, items: [{ ...item, valid: false, code: null }] }, false],
      [{ ...accepted, items: [{ ...item, evaluatedAt: 5 }] }, true],
      [{ ...accepted, items: [{ ...item, evidenceRef: 3 }] }, true],
    ];
    for (const [body, expected] of bad) expect(() => parseValidationResponse(body, expected)).toThrow(MalformedCitationResponse);
  });
});

describe("code text and failure classification", () => {
  it("explains known codes and shows unknown ones verbatim as a code", () => {
    expect(describeCitationCode("EVIDENCE_STALE")).toContain("stale");
    expect(describeCitationCode("NEW_CODE")).toBe("Rejection code: NEW_CODE");
  });
  it("classifies each failure honestly", () => {
    expect(classifyCitationFailure(new InvalidCitationInput("x")).kind).toBe("invalid");
    expect(classifyCitationFailure(new MalformedCitationResponse("x")).kind).toBe("malformed");
    expect(classifyCitationFailure({ status: 401 }).kind).toBe("unauthenticated");
    expect(classifyCitationFailure({ status: 403 }).kind).toBe("forbidden");
    expect(classifyCitationFailure({ status: 400 }).kind).toBe("bad-request");
    expect(classifyCitationFailure({ status: 503 }).kind).toBe("server");
    expect(classifyCitationFailure(new TypeError("fetch")).kind).toBe("offline");
    expect(classifyCitationFailure(new TypeError("fetch"), false).message).toContain("offline");
  });
});
