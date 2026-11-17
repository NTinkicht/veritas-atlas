import { describe, expect, it } from "vitest";
import { classifyFetchAttemptsFailure, MalformedFetchAttemptsResponse, parseFetchAttempts } from "./tenantFetchAttempts";

// SYNTHETIC fixtures only.
const ID1 = "11111111-1111-4111-8111-111111111111";
const ID2 = "22222222-2222-4222-8222-222222222222";
const ok = (id: string) => ({ id, requestedUri: "https://gazette.example/a", attemptedAtUtc: "2026-01-02T03:04:05Z", outcome: "succeeded", failureClass: null, httpStatus: 200 });
const bad = (id: string) => ({ ...ok(id), outcome: "failed", failureClass: "http_status", httpStatus: 503 });

describe("parseFetchAttempts", () => {
  it("accepts succeeded and failed rows and an empty list (positive control)", () => {
    const rows = parseFetchAttempts([ok(ID1), bad(ID2)]);
    expect(rows.map((r) => [r.outcome, r.failureClass, r.httpStatus])).toEqual([["succeeded", null, 200], ["failed", "http_status", 503]]);
    expect(parseFetchAttempts([])).toEqual([]);
  });

  it("does not carry fields beyond the ones it shows (no idempotency key)", () => {
    const [row] = parseFetchAttempts([{ ...ok(ID1), idempotencyKey: "SECRET", rawRetrievalId: ID2 }]);
    expect(JSON.stringify(row)).not.toContain("SECRET");
  });

  it.each([
    ["not a list", { items: [] }],
    ["item not an object", [null]],
    ["bad id", [{ ...ok(ID1), id: "x" }]],
    ["blank uri", [{ ...ok(ID1), requestedUri: " " }]],
    ["bad date", [{ ...ok(ID1), attemptedAtUtc: "yesterday" }]],
    ["unknown outcome", [{ ...ok(ID1), outcome: "pending" }]],
    ["unknown failure class", [{ ...bad(ID1), failureClass: "boom" }]],
    ["success with a failure class", [{ ...ok(ID1), failureClass: "network" }]],
    ["failure without a failure class", [{ ...bad(ID1), failureClass: null }]],
    ["status out of range", [{ ...ok(ID1), httpStatus: 99 }]],
    ["status not an integer", [{ ...ok(ID1), httpStatus: "200" }]],
    ["duplicate id (case-insensitive)", [ok(ID1), ok(ID1.toUpperCase())]],
    ["one bad row rejects the whole list", [ok(ID1), { ...ok(ID2), outcome: 5 }]],
  ])("rejects the whole response: %s", (_name, raw) => {
    expect(() => parseFetchAttempts(raw)).toThrow(MalformedFetchAttemptsResponse);
  });
});

describe("classifyFetchAttemptsFailure", () => {
  it("maps statuses to fixed messages and never echoes server text", () => {
    expect(classifyFetchAttemptsFailure({ status: 401, message: "SECRET" }).kind).toBe("unauthenticated");
    const forbidden = classifyFetchAttemptsFailure({ status: 403, message: "SECRET" });
    expect(forbidden).toEqual({ kind: "forbidden", message: expect.stringContaining("administrator") });
    const server = classifyFetchAttemptsFailure({ status: 500, message: "SECRET" });
    expect(server.message).toContain("HTTP 500");
    expect([forbidden, server].some((f) => f.message.includes("SECRET"))).toBe(false);
  });

  it("distinguishes offline and malformed", () => {
    expect(classifyFetchAttemptsFailure(new TypeError("x"), true).message).toContain("could not be reached");
    expect(classifyFetchAttemptsFailure(new TypeError("x"), false).message).toContain("offline");
    expect(classifyFetchAttemptsFailure(new MalformedFetchAttemptsResponse("x")).kind).toBe("malformed");
  });
});
