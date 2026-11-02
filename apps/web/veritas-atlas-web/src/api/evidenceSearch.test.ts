import { describe, expect, it } from "vitest";
import {
  buildSearchPath,
  classifyFailure,
  MalformedSearchResponse,
  parseSearchResponse,
  toFreshnessLabel,
} from "./evidenceSearch";

// SYNTHETIC wire sample: FreshnessState is serialised as a number by the API.
const hit = {
  evidenceRef: "synthetic-ref-1",
  sourceUri: "https://example.test/synthetic/1",
  sourceEntryId: "11111111-1111-4111-8111-111111111111",
  jurisdiction: "gb-uk",
  sourceType: "statute",
  attestationState: "Verified",
  retrievedAtUtc: "2026-11-02T09:00:00Z",
  freshness: 0,
  freshnessEligible: true,
  freshnessReason: null,
  freshnessEvaluatedAt: "2026-11-02T11:00:00Z",
};

describe("freshness mapping", () => {
  it("maps each numeric and named state", () => {
    expect([0, 1, 2, 3].map(toFreshnessLabel)).toEqual(["CURRENT", "STALE", "UNKNOWN", "SUPERSEDED"]);
    expect(["Current", "STALE", "superseded", "unknown"].map(toFreshnessLabel)).toEqual([
      "CURRENT",
      "STALE",
      "SUPERSEDED",
      "UNKNOWN",
    ]);
  });

  it("never turns an unrecognised value into CURRENT", () => {
    for (const value of [99, -1, 0.5, "", "fresh", "currently", null, undefined, {}, true]) {
      expect(toFreshnessLabel(value)).toBe("UNKNOWN");
    }
  });
});

describe("buildSearchPath", () => {
  it("encodes the query and sends only set filters", () => {
    const path = buildSearchPath({ q: "  a&b=c d ", freshness: "STALE", jurisdiction: " gb-uk ", limit: 20 });
    expect(path).toBe("/api/v1/tenant-evidence/search?q=a%26b%3Dc+d&freshness=stale&jurisdiction=gb-uk&limit=20");
  });

  it("never sends a tenant parameter", () => {
    expect(buildSearchPath({ q: "x", cursor: "abc" }).split("?")[1]).not.toMatch(/tenant/i);
  });
});

describe("parseSearchResponse", () => {
  it("parses hits and the next cursor", () => {
    const page = parseSearchResponse({ results: [hit], nextCursor: "c1" });
    expect(page.nextCursor).toBe("c1");
    expect(page.hits[0]).toMatchObject({ evidenceRef: "synthetic-ref-1", freshness: "CURRENT", freshnessReason: null });
  });

  it("treats a missing or empty cursor as the last page", () => {
    expect(parseSearchResponse({ results: [] }).nextCursor).toBeNull();
    expect(parseSearchResponse({ results: [], nextCursor: "" }).nextCursor).toBeNull();
  });

  it("rejects the whole page when any hit is malformed", () => {
    const bad = [
      null,
      "x",
      {},
      { results: "no" },
      { results: [hit, { ...hit, evidenceRef: 1 }] },
      { results: [{ ...hit, freshnessEligible: "yes" }] },
      { results: [{ ...hit, freshnessReason: 5 }] },
      { results: [hit], nextCursor: 7 },
    ];
    for (const body of bad) {
      expect(() => parseSearchResponse(body)).toThrow(MalformedSearchResponse);
    }
  });
});

describe("classifyFailure", () => {
  it("separates 401, 403, 400, other HTTP errors, malformed bodies and offline", () => {
    expect(classifyFailure({ status: 401, message: "x" }).kind).toBe("unauthenticated");
    expect(classifyFailure({ status: 403, message: "x" }).kind).toBe("forbidden");
    expect(classifyFailure({ status: 400, message: "x" }).kind).toBe("invalid");
    expect(classifyFailure({ status: 503, message: "x" }).kind).toBe("server");
    expect(classifyFailure(new MalformedSearchResponse("r")).kind).toBe("malformed");
    expect(classifyFailure(new TypeError("Failed to fetch"), false).kind).toBe("offline");
    expect(classifyFailure(new TypeError("Failed to fetch"), true).kind).toBe("offline");
  });
});
