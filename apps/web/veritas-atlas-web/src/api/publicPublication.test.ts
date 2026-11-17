import { afterEach, describe, expect, it, vi } from "vitest";
import { getPublicPublication, isSafeSourceUri, parsePublicPublication } from "./publicPublication";

const ID = "11111111-1111-4111-8111-111111111111";
const body = {
  id: ID,
  freshnessLabel: "CURRENT",
  humanReviewLabel: "independently-reviewed",
  citations: [
    { citationLabel: "S1", title: "T", quote: "Q", sourceName: "N", asOf: "2026-11-01T00:00:00Z", effectiveDate: null, freshnessLabel: "CURRENT" },
  ],
};
const respond = (status: number, json: unknown) =>
  vi.fn().mockResolvedValue({ ok: status >= 200 && status < 300, status, json: async () => json });

afterEach(() => {
  vi.unstubAllGlobals();
});

describe("getPublicPublication", () => {
  it("sends no credentials even when a token is stored", async () => {
    const store: Record<string, string> = { accessToken: "secret-token" };
    vi.stubGlobal("localStorage", { getItem: (k: string) => store[k] ?? null });
    const f = respond(200, body);
    vi.stubGlobal("fetch", f);
    const r = await getPublicPublication(ID);
    expect(r.kind).toBe("ok");
    const [url, init] = f.mock.calls[0] as [string, RequestInit];
    expect(url).toBe(`/api/v1/public/publications/${ID}`);
    expect(init.credentials).toBe("omit");
    expect(init.cache).toBe("no-store");
    expect(JSON.stringify(init.headers).toLowerCase()).not.toContain("authorization");
    expect(JSON.stringify(init)).not.toContain("secret-token");
  });

  it("returns hidden for 404 and for a malformed id without fetching", async () => {
    const f = respond(404, { error: "x" });
    vi.stubGlobal("fetch", f);
    expect((await getPublicPublication(ID)).kind).toBe("hidden");
    f.mockClear();
    expect((await getPublicPublication("../admin")).kind).toBe("hidden");
    expect(f).not.toHaveBeenCalled();
  });

  it("returns error for other statuses, network failure and malformed payloads", async () => {
    vi.stubGlobal("fetch", respond(500, {}));
    expect((await getPublicPublication(ID)).kind).toBe("error");
    vi.stubGlobal("fetch", vi.fn().mockRejectedValue(new Error("down")));
    expect((await getPublicPublication(ID)).kind).toBe("error");
    vi.stubGlobal("fetch", respond(200, { id: ID, freshnessLabel: "CURRENT", humanReviewLabel: "independently-reviewed", citations: [{ title: 1 }] }));
    expect((await getPublicPublication(ID)).kind).toBe("error");
  });
});

describe("parsePublicPublication disclosures", () => {
  it("keeps string disclosures and fails closed on any other type", () => {
    const c = { ...body.citations[0], contradictionDisclosure: "Conflicts with 2025", uncertaintyLimit: "Single source" };
    const parsed = parsePublicPublication({ ...body, citations: [c] });
    expect(parsed?.citations[0].contradictionDisclosure).toBe("Conflicts with 2025");
    expect(parsed?.citations[0].uncertaintyLimit).toBe("Single source");
    expect(parsePublicPublication({ ...body, citations: [{ ...c, contradictionDisclosure: 5 }] })).toBeNull();
    expect(parsePublicPublication({ ...body, citations: [{ ...c, uncertaintyLimit: { a: 1 } }] })).toBeNull();
  });
});

describe("parsePublicPublication", () => {
  it("accepts the allow-listed shape and rejects bad input", () => {
    expect(parsePublicPublication(body)?.citations[0].effectiveDate).toBeNull();
    expect(parsePublicPublication(body)?.citations[0].contradictionDisclosure).toBeNull();
    expect(parsePublicPublication(body)?.citations[0].uncertaintyLimit).toBeNull();
    expect(parsePublicPublication(null)).toBeNull();
    expect(parsePublicPublication({ ...body, id: "nope" })).toBeNull();
    expect(parsePublicPublication({ ...body, citations: "x" })).toBeNull();
    expect(parsePublicPublication({ ...body, citations: [{ ...body.citations[0], asOf: "garbage" }] })).toBeNull();
  });
});

describe("parsePublicPublication human review label", () => {
  it("accepts the exact label and fails closed when missing or different", () => {
    expect(parsePublicPublication(body)?.humanReviewLabel).toBe("independently-reviewed");
    expect(parsePublicPublication({ ...body, humanReviewLabel: undefined })).toBeNull();
    expect(parsePublicPublication({ ...body, humanReviewLabel: "unreviewed" })).toBeNull();
    expect(parsePublicPublication({ ...body, humanReviewLabel: true })).toBeNull();
  });
});

describe("parsePublicPublication confidence", () => {
  const withBand = (confidence: unknown) => ({ ...body, citations: [{ ...body.citations[0], confidence }] });

  it("keeps a known band, reads absent as null and fails closed on anything else", () => {
    for (const band of ["Insufficient", "Low", "Medium", "High"]) {
      expect(parsePublicPublication(withBand(band))?.citations[0].confidence).toBe(band);
    }
    expect(parsePublicPublication(body)?.citations[0].confidence).toBeNull();
    expect(parsePublicPublication(withBand(null))?.citations[0].confidence).toBeNull();
    for (const bad of ["high", "HIGH", "Certain", "3", "", 3, true, {}, ["High"]]) {
      expect(parsePublicPublication(withBand(bad)), String(bad)).toBeNull();
    }
  });
});

describe("parsePublicPublication source link", () => {
  const withUri = (sourceUri: unknown) => ({ ...body, citations: [{ ...body.citations[0], sourceUri }] });

  it("keeps a safe https link, reads absent as null and fails closed on anything else", () => {
    expect(parsePublicPublication(withUri("https://gazette.example.org/n/1"))?.citations[0].sourceUri).toBe("https://gazette.example.org/n/1");
    expect(parsePublicPublication(body)?.citations[0].sourceUri).toBeNull();
    expect(parsePublicPublication(withUri(null))?.citations[0].sourceUri).toBeNull();
    for (const bad of [
      "javascript:alert(1)", "http://gazette.example.org/n", "data:text/html,x", "//gazette.example.org/n", "/relative",
      "https://user:pw@gazette.example.org/n", "https://gazette.example.org/a b", "", 5, {}, `https://x.example/${"p".repeat(2100)}`,
    ]) {
      expect(parsePublicPublication(withUri(bad)), String(bad)).toBeNull();
    }
  });

  it("exposes the same predicate for callers", () => {
    expect(isSafeSourceUri("https://a.example/b")).toBe(true);
    expect(isSafeSourceUri("http://a.example/b")).toBe(false);
  });
});
