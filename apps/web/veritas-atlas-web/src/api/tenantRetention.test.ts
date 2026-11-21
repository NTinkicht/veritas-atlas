import { describe, expect, it } from "vitest";
import {
  classifyRetentionFailure,
  MalformedRetentionResponse,
  parseRetentionEligibility,
  parseRetentionHistory,
  retentionPath,
} from "./tenantRetention";

const good = { outcome: "Deny", denialCode: "legal_hold", parseRunCount: 1, attestationCount: 2, recordVersion: 3, legalHold: true, retainUntilUtc: "2027-01-02T03:04:05Z" };

describe("retention parsing", () => {
  it("accepts a deny verdict with a known code and an allow verdict without one", () => {
    expect(parseRetentionEligibility(good).denialCode).toBe("legal_hold");
    expect(parseRetentionEligibility({ ...good, outcome: "Allow", denialCode: null, legalHold: false }).outcome).toBe("Allow");
  });

  it.each([
    ["unknown outcome", { ...good, outcome: "Maybe" }],
    ["unknown denial code", { ...good, denialCode: "made_up" }],
    ["deny without a code", { ...good, denialCode: null }],
    ["allow with a code", { ...good, outcome: "Allow" }],
    ["negative count", { ...good, parseRunCount: -1 }],
    ["string flag", { ...good, legalHold: "true" }],
    ["bad instant", { ...good, retainUntilUtc: "soon" }],
  ])("rejects %s whole", (_n, body) => {
    expect(() => parseRetentionEligibility(body)).toThrow(MalformedRetentionResponse);
  });

  it("parses history and rejects a blank actor or a non-list", () => {
    const v = { version: 1, legalHold: false, retainUntilUtc: null, actorSubject: "admin", createdAtUtc: "2026-01-01T00:00:00Z" };
    expect(parseRetentionHistory({ versions: [v] })[0].retainUntilUtc).toBeNull();
    expect(() => parseRetentionHistory({ versions: [{ ...v, actorSubject: " " }] })).toThrow(MalformedRetentionResponse);
    expect(() => parseRetentionHistory({ versions: {} })).toThrow(MalformedRetentionResponse);
    expect(() => parseRetentionHistory([])).toThrow(MalformedRetentionResponse);
  });

  it("only builds paths for a guid and never echoes server text", () => {
    expect(retentionPath("11111111-1111-4111-8111-111111111111", "retention")).toBe("/api/v1/tenant-raw-retrievals/11111111-1111-4111-8111-111111111111/retention");
    expect(() => retentionPath("../x", "retention")).toThrow(MalformedRetentionResponse);
    expect(classifyRetentionFailure({ status: 404, message: "SECRET" }).message).not.toContain("SECRET");
    expect(classifyRetentionFailure({ status: 403 }).kind).toBe("forbidden");
    expect(classifyRetentionFailure({ status: 500 }).kind).toBe("server");
    expect(classifyRetentionFailure(new Error("x"), false).message).toContain("offline");
  });
});
