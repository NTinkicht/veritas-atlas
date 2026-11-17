import { describe, expect, it } from "vitest";
import { buildVisibilityBody, visibilityPath, VISIBILITY_LIMITS, type VisibilityInput } from "./publicationVisibility";

// SYNTHETIC fixtures only.
const cite = { citationLabel: "C1", title: "T", quote: "Q", sourceName: "S", freshnessLabel: "Current as of 2026" };
const ok: VisibilityInput = {
  restricted: false, superseded: false, freshnessRequired: true, evidenceCurrency: "Current", historicalLabelPresent: false,
  citations: [cite],
};
const id = "11111111-1111-4111-8111-111111111111";

describe("buildVisibilityBody", () => {
  it("builds the exact server body", () => {
    const body = buildVisibilityBody(ok, 4, "op-1");
    expect(body).toMatchObject({ expectedFactsVersion: 4, operationId: "op-1", restricted: false, evidenceCurrency: "Current", expiresAt: null });
    expect(body.citations[0]).toMatchObject({ ...cite, asOf: null, contradictionDisclosure: null });
  });
  it("refuses an unrestricted revision with no citation", () => {
    expect(() => buildVisibilityBody({ ...ok, citations: [] }, 0, "op")).toThrow(/at least one citation/);
  });
  it("allows a restricted revision with no citation", () => {
    expect(buildVisibilityBody({ ...ok, restricted: true, citations: [] }, 0, "op").citations).toEqual([]);
  });
  it.each([
    ["empty quote", { ...cite, quote: "  " }],
    ["long title", { ...cite, title: "x".repeat(VISIBILITY_LIMITS.title + 1) }],
    ["control char in label", { ...cite, citationLabel: "a\u0000b" }],
    ["newline in single-line source", { ...cite, sourceName: "a\nb" }],
    ["bad date", { ...cite, asOf: "not-a-date" }],
    ["long disclosure", { ...cite, uncertaintyLimit: "x".repeat(VISIBILITY_LIMITS.disclosure + 1) }],
  ])("rejects %s", (_n, c) => {
    expect(() => buildVisibilityBody({ ...ok, citations: [c] }, 0, "op")).toThrow();
  });
  it("rejects too many citations, bad version, reserved or empty operation id, unknown currency", () => {
    expect(() => buildVisibilityBody({ ...ok, citations: Array(51).fill(cite) }, 0, "op")).toThrow();
    expect(() => buildVisibilityBody(ok, -1, "op")).toThrow();
    expect(() => buildVisibilityBody(ok, 0, "transition:x")).toThrow();
    expect(() => buildVisibilityBody(ok, 0, "")).toThrow();
    expect(() => buildVisibilityBody({ ...ok, evidenceCurrency: "Bogus" as never }, 0, "op")).toThrow();
  });
  it("targets the revision visibility path and rejects non-GUID ids", () => {
    expect(visibilityPath(id)).toBe(`/api/v1/tenant-publication-revisions/${id}/visibility`);
    expect(() => visibilityPath("../x")).toThrow();
  });
});
