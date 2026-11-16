import { describe, expect, it } from "vitest";
import {
  InvalidClassifyInput,
  MalformedClassifyResponse,
  buildClassifyBody,
  classifyClassificationFailure,
  parseClassifyResponse,
} from "./statementClassification";

// SYNTHETIC fixtures only.
const fresh = { state: "current", eligible: true, reason: null, evaluatedAt: "2026-11-16T10:00:00Z" };
const ok = (cls: string, extra: Record<string, unknown> = {}) => ({
  evidenceRef: "ev1:abc", classifierName: "statement-classifier", classifierVersion: "1", class: cls,
  supportingEvidenceRef: null, supportingElementId: null, span: null, answerable: false, freshness: fresh, ...extra,
});
const span = { elementId: "s1", chunkTextSha256: "dead", start: 3, length: 12 };

describe("classification request body", () => {
  it("trims both fields", () => {
    expect(buildClassifyBody({ evidenceRef: " ev1:abc ", statement: " The register lists X. " })).toEqual({ evidenceRef: "ev1:abc", statement: "The register lists X." });
  });
  it("refuses blank, over-long and control-character input before any request", () => {
    expect(() => buildClassifyBody({ evidenceRef: "", statement: "x" })).toThrow(InvalidClassifyInput);
    expect(() => buildClassifyBody({ evidenceRef: "ev1:a", statement: "  " })).toThrow(InvalidClassifyInput);
    expect(() => buildClassifyBody({ evidenceRef: "ev1:a", statement: "x".repeat(2001) })).toThrow(InvalidClassifyInput);
    expect(() => buildClassifyBody({ evidenceRef: "ev1:a", statement: "a\u0007b" })).toThrow(InvalidClassifyInput);
    expect(buildClassifyBody({ evidenceRef: "ev1:a", statement: "x".repeat(2000) }).statement).toHaveLength(2000);
  });
});

describe("classification response", () => {
  it("parses each of the four classes, a quotation with its span", () => {
    expect(parseClassifyResponse(ok("quotation", { span, supportingEvidenceRef: "ev1:abc" }), "ev1:abc")).toMatchObject({
      statementClass: "quotation", span: { elementId: "s1", start: 3, length: 12 }, supportingEvidenceRef: "ev1:abc",
    });
    for (const c of ["inference", "unresolved", "unsupported"]) {
      expect(parseClassifyResponse(ok(c), "ev1:abc").statementClass).toBe(c);
    }
  });
  it("rejects a malformed response whole (positive control above)", () => {
    const bad = (raw: unknown) => expect(() => parseClassifyResponse(raw, "ev1:abc")).toThrow(MalformedClassifyResponse);
    bad(ok("guess"));
    bad(ok("inference", { answerable: true }));
    bad(ok("inference", { answerable: undefined }));
    bad(ok("quotation"));
    bad(ok("inference", { freshness: undefined }));
    bad(ok("inference", { freshness: { state: "current" } }));
    bad(ok("inference", { evidenceRef: "ev1:other" }));
    bad(ok("quotation", { span: { elementId: "s1", start: -1, length: 2 } }));
    bad(null);
  });
});

describe("failure classification", () => {
  it("maps statuses to fixed messages that never echo input", () => {
    const kind = (status: unknown) => classifyClassificationFailure({ status, message: "SECRET statement" }).kind;
    expect(kind(401)).toBe("unauthenticated");
    expect(kind(403)).toBe("forbidden");
    expect(kind(404)).toBe("not-found");
    expect(kind(409)).toBe("refused");
    expect(kind(400)).toBe("bad-request");
    expect(kind(503)).toBe("server");
    expect(classifyClassificationFailure({ status: 404, message: "SECRET statement" }).message).not.toContain("SECRET");
    expect(classifyClassificationFailure(new TypeError("x"), false).kind).toBe("offline");
    expect(classifyClassificationFailure(new InvalidClassifyInput("bad")).kind).toBe("invalid");
  });
});
