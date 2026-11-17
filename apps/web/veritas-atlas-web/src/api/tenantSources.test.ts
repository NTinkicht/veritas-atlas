import { describe, expect, it } from "vitest";
import { classifyTenantSourcesFailure, MalformedTenantSourcesResponse, parseTenantSources } from "./tenantSources";

// SYNTHETIC fixtures only.
const ID1 = "11111111-1111-4111-8111-111111111111";
const ID2 = "22222222-2222-4222-8222-222222222222";
const item = (id: string, canonicalKey = "gazette.example/register") => ({ id, canonicalKey, createdAtUtc: "2026-01-02T03:04:05Z" });

describe("parseTenantSources", () => {
  it("accepts a valid list and an empty list (positive control)", () => {
    expect(parseTenantSources({ items: [item(ID1), item(ID2, "other.example")] })).toEqual([
      { id: ID1, canonicalKey: "gazette.example/register", createdAtUtc: "2026-01-02T03:04:05Z" },
      { id: ID2, canonicalKey: "other.example", createdAtUtc: "2026-01-02T03:04:05Z" },
    ]);
    expect(parseTenantSources({ items: [] })).toEqual([]);
  });

  it.each([
    ["not an object", null],
    ["no items", {}],
    ["items not array", { items: "x" }],
    ["item not an object", { items: [null] }],
    ["bad id", { items: [item("not-a-guid")] }],
    ["blank canonicalKey", { items: [item(ID1, "  ")] }],
    ["non-string canonicalKey", { items: [{ ...item(ID1), canonicalKey: 5 }] }],
    ["bad date", { items: [{ ...item(ID1), createdAtUtc: "yesterday" }] }],
    ["duplicate id (case-insensitive)", { items: [item(ID1), item(ID1.toUpperCase())] }],
    ["one bad item rejects the whole list", { items: [item(ID1), item("nope")] }],
  ])("rejects the whole response: %s", (_name, raw) => {
    expect(() => parseTenantSources(raw)).toThrow(MalformedTenantSourcesResponse);
  });
});

describe("classifyTenantSourcesFailure", () => {
  it("maps statuses to fixed messages and never echoes server text", () => {
    expect(classifyTenantSourcesFailure({ status: 401, message: "SECRET" }).kind).toBe("unauthenticated");
    const forbidden = classifyTenantSourcesFailure({ status: 403, message: "SECRET" });
    expect(forbidden.kind).toBe("forbidden");
    const server = classifyTenantSourcesFailure({ status: 500, message: "SECRET" });
    expect(server).toEqual({ kind: "server", message: expect.stringContaining("HTTP 500") });
    expect([forbidden, server].some((f) => f.message.includes("SECRET"))).toBe(false);
  });

  it("distinguishes offline and malformed", () => {
    expect(classifyTenantSourcesFailure(new TypeError("x"), true).message).toContain("could not be reached");
    expect(classifyTenantSourcesFailure(new TypeError("x"), false).message).toContain("offline");
    expect(classifyTenantSourcesFailure(new MalformedTenantSourcesResponse("items")).kind).toBe("malformed");
  });
});
