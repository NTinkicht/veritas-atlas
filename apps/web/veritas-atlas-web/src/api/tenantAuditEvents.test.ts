import { describe, expect, it } from "vitest";
import {
  AUDIT_EVENTS_PATH,
  InvalidAuditEventFilter,
  MalformedAuditEventsResponse,
  auditEventsPath,
  classifyAuditEventsFailure,
  parseAuditEvents,
} from "./tenantAuditEvents";

// SYNTHETIC fixtures only.
const good = {
  id: "6f1c2d3e-0000-4000-8000-000000000001",
  action: "publication.approved",
  entityType: "publication",
  entityId: "p-1",
  actorSubject: "admin",
  occurredAtUtc: "2027-01-02T03:04:05Z",
};

describe("audit event parsing", () => {
  it("accepts a valid list, including an empty one", () => {
    expect(parseAuditEvents([good])[0].action).toBe("publication.approved");
    expect(parseAuditEvents([])).toEqual([]);
  });

  it.each([
    ["non-array", { events: [] }],
    ["null item", [null]],
    ["array item", [[]]],
    ["bad id", [{ ...good, id: "nope" }]],
    ["blank action", [{ ...good, action: " " }]],
    ["numeric entityId", [{ ...good, entityId: 7 }]],
    ["missing actor", [{ ...good, actorSubject: undefined }]],
    ["bad instant", [{ ...good, occurredAtUtc: "soon" }]],
  ])("rejects %s whole", (_n, body) => {
    expect(() => parseAuditEvents(body)).toThrow(MalformedAuditEventsResponse);
  });

  it("rejects the whole list when one item is bad", () => {
    expect(() => parseAuditEvents([good, { ...good, action: "" }])).toThrow(
      MalformedAuditEventsResponse,
    );
  });
});

describe("audit event filter path", () => {
  it("uses the bare path without filters and encodes filters when present", () => {
    expect(auditEventsPath()).toBe(AUDIT_EVENTS_PATH);
    expect(auditEventsPath({ entityType: " ", entityId: "" })).toBe(
      AUDIT_EVENTS_PATH,
    );
    expect(
      auditEventsPath({ entityType: "publication", entityId: "a b&c" }),
    ).toBe(`${AUDIT_EVENTS_PATH}?entityType=publication&entityId=a+b%26c`);
  });

  it("rejects an entity id without a type, control characters and overlong values", () => {
    expect(() => auditEventsPath({ entityId: "p-1" })).toThrow(
      InvalidAuditEventFilter,
    );
    expect(() => auditEventsPath({ entityType: "a\nb" })).toThrow(
      InvalidAuditEventFilter,
    );
    expect(() => auditEventsPath({ entityType: "x".repeat(129) })).toThrow(
      InvalidAuditEventFilter,
    );
    expect(() =>
      auditEventsPath({ entityType: "x", entityId: "y".repeat(257) }),
    ).toThrow(InvalidAuditEventFilter);
    expect(() =>
      auditEventsPath({
        entityType: "x".repeat(128),
        entityId: "y".repeat(256),
      }),
    ).not.toThrow();
  });
});

describe("audit event failures", () => {
  it("maps statuses to fixed messages and never echoes server text", () => {
    expect(
      classifyAuditEventsFailure({ status: 401, message: "secret" }).kind,
    ).toBe("unauthenticated");
    const forbidden = classifyAuditEventsFailure({
      status: 403,
      message: "secret detail",
    });
    expect(forbidden.kind).toBe("forbidden");
    expect(forbidden.message).not.toContain("secret");
    expect(classifyAuditEventsFailure({ status: 400 }).kind).toBe(
      "bad-request",
    );
    expect(
      classifyAuditEventsFailure({ status: 500, message: "stack" }),
    ).toEqual({
      kind: "server",
      message: expect.not.stringContaining("stack"),
    });
    expect(classifyAuditEventsFailure(new TypeError("x")).kind).toBe("offline");
    expect(
      classifyAuditEventsFailure(new TypeError("x"), false).message,
    ).toContain("offline");
    expect(
      classifyAuditEventsFailure(new MalformedAuditEventsResponse("list")).kind,
    ).toBe("malformed");
    expect(
      classifyAuditEventsFailure(new InvalidAuditEventFilter("bad")).kind,
    ).toBe("invalid");
  });
});
