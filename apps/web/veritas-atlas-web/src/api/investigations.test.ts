import { describe, expect, it } from "vitest";
import {
  MalformedInvestigationResponse,
  classifyInvestigationFailure,
  comparisonPath,
  evidenceItemsPath,
  parseEvidenceComparison,
  parseEvidenceItems,
  parseInvestigations,
} from "./investigations";

// SYNTHETIC fixtures only.
const ID = "11111111-1111-4111-8111-111111111111";
const EV = "22222222-2222-4222-8222-222222222222";
const inv = { id: ID, title: "Synthetic inquiry", createdBySubject: "analyst-1", createdAtUtc: "2026-11-02T09:00:00Z" };
const item = { id: ID, evidenceVerificationId: EV, note: "n", collectedBySubject: "analyst-1", createdAtUtc: "2026-11-02T09:00:00Z" };

describe("investigation client parsing", () => {
  it("parses a valid investigation list", () => {
    expect(parseInvestigations({ items: [inv] })).toEqual([inv]);
  });
  it("rejects the whole list when one item is malformed", () => {
    expect(() => parseInvestigations({ items: [inv, { ...inv, id: "not-a-guid" }] })).toThrow(MalformedInvestigationResponse);
    expect(() => parseInvestigations({ items: [{ ...inv, title: "" }] })).toThrow(MalformedInvestigationResponse);
    expect(() => parseInvestigations({})).toThrow(MalformedInvestigationResponse);
    expect(() => parseInvestigations(null)).toThrow(MalformedInvestigationResponse);
  });
  it("parses evidence items and normalises an empty note to null", () => {
    expect(parseEvidenceItems({ items: [item, { ...item, note: "" }, { ...item, note: null }] }).map((i) => i.note)).toEqual(["n", null, null]);
  });
  it("rejects a non-text note or non-GUID evidence id", () => {
    expect(() => parseEvidenceItems({ items: [{ ...item, note: 5 }] })).toThrow(MalformedInvestigationResponse);
    expect(() => parseEvidenceItems({ items: [{ ...item, evidenceVerificationId: "x" }] })).toThrow(MalformedInvestigationResponse);
  });
  it("refuses to build a path from a non-GUID investigation id", () => {
    expect(evidenceItemsPath(ID)).toBe(`/api/v1/tenant-investigations/${ID}/evidence-items`);
    expect(() => evidenceItemsPath("../admin")).toThrow(MalformedInvestigationResponse);
  });
});

describe("investigation failure classification", () => {
  it.each([
    [401, "unauthenticated"],
    [403, "forbidden"],
    [404, "not-found"],
    [500, "server"],
  ])("maps HTTP %i to %s", (status, kind) => {
    expect(classifyInvestigationFailure({ status }).kind).toBe(kind);
  });
  it("maps network failure to offline and malformed to malformed", () => {
    expect(classifyInvestigationFailure(new Error("x"), false).kind).toBe("offline");
    expect(classifyInvestigationFailure(new MalformedInvestigationResponse("d")).kind).toBe("malformed");
  });
});

describe("evidence comparison client", () => {
  const L = "33333333-3333-4333-8333-333333333333";
  const R = "44444444-4444-4444-8444-444444444444";
  const side = (itemId: string, extra: Record<string, unknown> = {}) => ({
    itemId,
    evidenceVerificationId: EV,
    note: null,
    parentEvidenceRef: "ev1:aaa",
    rawRetrievalId: ID,
    attestationId: ID,
    parseRunId: ID,
    verifiedAtUtc: "2026-11-02T09:00:00Z",
    ...extra,
  });
  const body = (over: Record<string, unknown> = {}) => ({
    left: side(L),
    right: side(R, { parentEvidenceRef: "ev1:bbb" }),
    sameEvidenceRef: false,
    sameRawRetrieval: true,
    sameAttestation: true,
    sameParseRun: true,
    ...over,
  });
  it("parses a consistent comparison", () => {
    const c = parseEvidenceComparison(body());
    expect(c.left.itemId).toBe(L);
    expect(c.sameEvidenceRef).toBe(false);
  });
  it("rejects a response whose flags contradict the compared items", () => {
    expect(() => parseEvidenceComparison(body({ sameEvidenceRef: true }))).toThrow(MalformedInvestigationResponse);
    expect(() => parseEvidenceComparison(body({ sameParseRun: false }))).toThrow(MalformedInvestigationResponse);
  });
  it("rejects missing sides, non-boolean flags and bad ids", () => {
    expect(() => parseEvidenceComparison({})).toThrow(MalformedInvestigationResponse);
    expect(() => parseEvidenceComparison(body({ sameParseRun: "true" }))).toThrow(MalformedInvestigationResponse);
    expect(() => parseEvidenceComparison(body({ left: side("x") }))).toThrow(MalformedInvestigationResponse);
    expect(() => parseEvidenceComparison(null)).toThrow(MalformedInvestigationResponse);
  });
  it("builds only GUID, two-distinct-item paths", () => {
    expect(comparisonPath(ID, L, R)).toBe(`/api/v1/tenant-investigations/${ID}/evidence-items/comparison?left=${L}&right=${R}`);
    expect(() => comparisonPath(ID, L, L)).toThrow(MalformedInvestigationResponse);
    expect(() => comparisonPath(ID, L, "a&b=c")).toThrow(MalformedInvestigationResponse);
    expect(() => comparisonPath("../x", L, R)).toThrow(MalformedInvestigationResponse);
  });
});
