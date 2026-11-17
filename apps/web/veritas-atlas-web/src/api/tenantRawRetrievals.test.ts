import { describe, expect, it } from "vitest";
import { classifyRawRetrievalsFailure, MalformedRawRetrievalsResponse, parseRawRetrievals } from "./tenantRawRetrievals";

// SYNTHETIC fixtures only.
const ID1 = "11111111-1111-4111-8111-111111111111";
const ID2 = "22222222-2222-4222-8222-222222222222";
const SHA = "a".repeat(64);
const ok = (id: string) => ({ id, sourceEntryId: ID2, finalUri: "https://gazette.example/a", contentSha256: SHA, byteLength: 12, retrievedAtUtc: "2026-01-02T03:04:05Z", createdAtUtc: "2026-01-02T03:04:06Z" });

describe("parseRawRetrievals", () => {
  it("accepts rows and an empty list (positive control)", () => {
    expect(parseRawRetrievals([ok(ID1), ok(ID2)]).map((r) => [r.id, r.byteLength])).toEqual([[ID1, 12], [ID2, 12]]);
    expect(parseRawRetrievals([])).toEqual([]);
  });

  it("keeps only the fields it shows", () => {
    const [row] = parseRawRetrievals([{ ...ok(ID1), idempotencyKey: "SECRET", evidenceRef: "ev1:x" }]);
    expect(JSON.stringify(row)).not.toContain("SECRET");
    expect(JSON.stringify(row)).not.toContain("ev1:");
  });

  it.each([
    ["not a list", { items: [] }],
    ["item not an object", [null]],
    ["bad id", [{ ...ok(ID1), id: "x" }]],
    ["blank uri", [{ ...ok(ID1), finalUri: " " }]],
    ["short digest", [{ ...ok(ID1), contentSha256: "abc" }]],
    ["non-hex digest", [{ ...ok(ID1), contentSha256: "z".repeat(64) }]],
    ["negative length", [{ ...ok(ID1), byteLength: -1 }]],
    ["fractional length", [{ ...ok(ID1), byteLength: 1.5 }]],
    ["bad date", [{ ...ok(ID1), retrievedAtUtc: "yesterday" }]],
    ["duplicate id", [ok(ID1), ok(ID1.toUpperCase())]],
  ])("rejects the whole response: %s", (_name, raw) => {
    expect(() => parseRawRetrievals(raw)).toThrow(MalformedRawRetrievalsResponse);
  });
});

describe("classifyRawRetrievalsFailure", () => {
  it("maps statuses to fixed messages and never echoes server text", () => {
    expect(classifyRawRetrievalsFailure({ status: 401 }).kind).toBe("unauthenticated");
    const forbidden = classifyRawRetrievalsFailure({ status: 403, message: "SECRET" });
    expect(forbidden.kind).toBe("forbidden");
    expect(forbidden.message).not.toContain("SECRET");
    expect(classifyRawRetrievalsFailure({ status: 500 }).message).toContain("HTTP 500");
    expect(classifyRawRetrievalsFailure(new Error("x"), false).message).toContain("offline");
    expect(classifyRawRetrievalsFailure(new MalformedRawRetrievalsResponse("item 0")).kind).toBe("malformed");
  });
});
