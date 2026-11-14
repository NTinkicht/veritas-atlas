import { describe, expect, it } from "vitest";
import {
  InvalidConfidenceInput,
  MalformedConfidenceResponse,
  buildConfidenceBody,
  classifyConfidenceFailure,
  describeReason,
  parseConfidenceResponse,
} from "./claimConfidence";

// SYNTHETIC fixtures only.
const ok = { evidenceVerified: false, labels: [{ claimId: "c-1", band: "medium", reasons: ["single_independent_source"] }] };

describe("confidence request body", () => {
  it("omits blank counts instead of defaulting them to 0", () => {
    expect(buildConfidenceBody(" c-1 ", { supportingEvidenceCount: "2", staleCount: "", conflictingCount: "0" })).toEqual({
      claims: [{ claimId: "c-1", supportingEvidenceCount: 2, conflictingCount: 0 }],
    });
    expect(buildConfidenceBody("c-1", {})).toEqual({ claims: [{ claimId: "c-1" }] });
  });
  it("refuses bad ids and counts before any request", () => {
    expect(() => buildConfidenceBody("  ", {})).toThrow(InvalidConfidenceInput);
    expect(() => buildConfidenceBody("a\nb", {})).toThrow(InvalidConfidenceInput);
    expect(() => buildConfidenceBody("x".repeat(501), {})).toThrow(InvalidConfidenceInput);
    expect(() => buildConfidenceBody("c", { staleCount: "-1" })).toThrow(InvalidConfidenceInput);
    expect(() => buildConfidenceBody("c", { staleCount: "1.5" })).toThrow(InvalidConfidenceInput);
    expect(() => buildConfidenceBody("c", { staleCount: "100001" })).toThrow(InvalidConfidenceInput);
    expect(buildConfidenceBody("c", { staleCount: "100000" }).claims[0]).toMatchObject({ staleCount: 100000 });
  });
});

describe("confidence response parsing", () => {
  it("parses a valid response", () => {
    expect(parseConfidenceResponse(ok, "c-1")).toEqual({ claimId: "c-1", band: "medium", reasons: ["single_independent_source"] });
  });
  it("rejects the whole response when anything is off", () => {
    const bad = (patch: object) => () => parseConfidenceResponse({ ...ok, ...patch }, "c-1");
    expect(bad({ evidenceVerified: true })).toThrow(MalformedConfidenceResponse);
    expect(bad({ labels: [] })).toThrow(MalformedConfidenceResponse);
    expect(bad({ labels: [{ ...ok.labels[0], band: "certain" }] })).toThrow(MalformedConfidenceResponse);
    expect(bad({ labels: [{ ...ok.labels[0], reasons: "x" }] })).toThrow(MalformedConfidenceResponse);
    expect(bad({ labels: [{ ...ok.labels[0], claimId: "other" }] })).toThrow(MalformedConfidenceResponse);
    expect(() => parseConfidenceResponse(null, "c-1")).toThrow(MalformedConfidenceResponse);
  });
  it("describes known reasons and falls back to the code", () => {
    expect(describeReason("stale_evidence")).toContain("stale");
    expect(describeReason("new_code")).toBe("Reason code: new_code");
  });
});

describe("confidence failure classification", () => {
  it.each([
    [401, "unauthenticated"],
    [403, "forbidden"],
    [400, "bad-request"],
    [500, "server"],
  ])("maps HTTP %i to %s", (status, kind) => {
    expect(classifyConfidenceFailure({ status }).kind).toBe(kind);
  });
  it("maps network failures to offline and bad input to invalid", () => {
    expect(classifyConfidenceFailure(new TypeError("x"), false).message).toContain("offline");
    expect(classifyConfidenceFailure(new InvalidConfidenceInput("bad")).kind).toBe("invalid");
  });
});
