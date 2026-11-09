import { describe, expect, it } from "vitest";
import {
  MalformedInvestigationResponse,
  classifyInvestigationFailure,
  evidenceItemsPath,
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
