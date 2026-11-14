// @vitest-environment jsdom
import { afterEach, describe, expect, it, vi } from "vitest";
import { MalformedCitationResponse, inspectCitations } from "./citationInspector";

// SYNTHETIC fixtures only. Exercises the real http layer against a stubbed fetch.
const policy = { evidenceRequired: true, historicalEvidenceAllowed: false, historicalTimeContextPreserved: false };
const rows = [{ evidenceRef: "ev1:abc", parserName: "p", parserVersion: "1", scopeSourceEntryId: "" }];
const item = { evidenceRef: "ev1:abc", valid: true, code: null, freshness: 0, evaluatedAt: null, historicalAsOf: null };

function stub(status: number, body: unknown) {
  const fetchMock = vi.fn().mockResolvedValue(new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } }));
  vi.stubGlobal("fetch", fetchMock);
  return fetchMock;
}
afterEach(() => vi.unstubAllGlobals());

describe("inspectCitations", () => {
  it("posts once to the validator and returns an accepted verdict", async () => {
    const fetchMock = stub(200, { valid: true, code: null, items: [item] });
    const verdict = await inspectCitations(policy, rows);
    expect(verdict.valid).toBe(true);
    expect(fetchMock).toHaveBeenCalledTimes(1);
    expect(String(fetchMock.mock.calls[0][0])).toContain("/api/v1/tenant-claims/validate-evidence");
  });
  it("turns a 422 into a rejected verdict carrying the stable code", async () => {
    stub(422, { valid: false, code: "EVIDENCE_REQUIRED", items: [] });
    expect(await inspectCitations(policy, [])).toEqual({ valid: false, code: "EVIDENCE_REQUIRED", items: [] });
  });
  it("rejects a 422 whose body claims validity, and propagates other statuses", async () => {
    stub(422, { valid: true, code: null, items: [] });
    await expect(inspectCitations(policy, [])).rejects.toBeInstanceOf(MalformedCitationResponse);
    stub(403, { code: "tenant_required", message: "no tenant" });
    await expect(inspectCitations(policy, [])).rejects.toMatchObject({ status: 403 });
    stub(200, { valid: false, code: "EVIDENCE_REQUIRED", items: [] });
    await expect(inspectCitations(policy, [])).rejects.toBeInstanceOf(MalformedCitationResponse);
  });
  it("sends nothing when the input is invalid", async () => {
    const fetchMock = stub(200, {});
    await expect(inspectCitations(policy, [{ ...rows[0], evidenceRef: "" }])).rejects.toThrow();
    expect(fetchMock).not.toHaveBeenCalled();
  });
});
