import { describe, expect, it } from "vitest";
import { buildDetailPath, classifyDetailFailure, MalformedDetailResponse, parseDetailResponse } from "./evidenceDetail";

// SYNTHETIC fixtures only.
const REF = "ev1:synthetic-ref";
const good = (over: Record<string, unknown> = {}) => ({
  evidenceRef: REF,
  sourceUri: "https://alpha.example/r1",
  sourceEntryId: "11111111-1111-4111-8111-111111111111",
  jurisdiction: "XX",
  sourceType: "gazette",
  attestationState: "Verified",
  retrievedAtUtc: "2026-02-03T04:05:06Z",
  freshness: 0,
  freshnessEligible: true,
  freshnessReason: null,
  freshnessEvaluatedAt: "2026-02-03T04:05:07Z",
  parserName: "p",
  parserVersion: "1",
  chunks: [{ ordinal: 0, elementId: "e0", text: "hello" }],
  chunksTruncated: false,
  ...over,
});

describe("parseDetailResponse", () => {
  it("accepts a well-formed detail and maps freshness", () => {
    const d = parseDetailResponse(good(), REF);
    expect(d.freshness).toBe("CURRENT");
    expect(d.chunks).toHaveLength(1);
  });
  it("maps an unknown freshness value to UNKNOWN, never CURRENT", () => {
    expect(parseDetailResponse(good({ freshness: 99 })).freshness).toBe("UNKNOWN");
    expect(parseDetailResponse(good({ freshness: 1 })).freshness).toBe("STALE");
  });
  it.each([
    ["not an object", "x"],
    ["missing text", good({ sourceUri: undefined })],
    ["eligible not boolean", good({ freshnessEligible: "yes" })],
    ["chunks missing", good({ chunks: undefined })],
    ["chunk text not text", good({ chunks: [{ ordinal: 0, elementId: "e", text: 5 }] })],
    ["truncated not boolean", good({ chunksTruncated: null })],
    ["reason not text", good({ freshnessReason: 3 })],
    ["different reference than requested", good({ evidenceRef: "ev1:other" })],
  ])("rejects the whole response: %s", (_name, raw) => {
    expect(() => parseDetailResponse(raw, REF)).toThrow(MalformedDetailResponse);
  });
});

describe("detail path and failures", () => {
  it("url-encodes the reference", () => {
    expect(buildDetailPath("ev1:a/b?c")).toBe("/api/v1/tenant-evidence/detail?ref=ev1%3Aa%2Fb%3Fc");
  });
  it.each([
    [401, "unauthenticated"],
    [403, "forbidden"],
    [404, "notfound"],
    [400, "invalid"],
    [500, "server"],
  ])("maps HTTP %i to %s with a fixed message", (status, kind) => {
    const f = classifyDetailFailure({ status, message: "SERVER SECRET" });
    expect(f.kind).toBe(kind);
    expect(f.message).not.toContain("SERVER SECRET");
  });
  it("maps no status to offline", () => {
    expect(classifyDetailFailure(new TypeError("x"), false).kind).toBe("offline");
  });
});
