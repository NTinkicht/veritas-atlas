import { afterEach, describe, expect, it, vi } from "vitest";
import { getPublicPublication, parsePublicPublication } from "./publicPublication";

const ID = "11111111-1111-4111-8111-111111111111";
const body = {
  id: ID,
  freshnessLabel: "CURRENT",
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
    vi.stubGlobal("fetch", respond(200, { id: ID, freshnessLabel: "CURRENT", citations: [{ title: 1 }] }));
    expect((await getPublicPublication(ID)).kind).toBe("error");
  });
});

describe("parsePublicPublication", () => {
  it("accepts the allow-listed shape and rejects bad input", () => {
    expect(parsePublicPublication(body)?.citations[0].effectiveDate).toBeNull();
    expect(parsePublicPublication(null)).toBeNull();
    expect(parsePublicPublication({ ...body, id: "nope" })).toBeNull();
    expect(parsePublicPublication({ ...body, citations: "x" })).toBeNull();
    expect(parsePublicPublication({ ...body, citations: [{ ...body.citations[0], asOf: "garbage" }] })).toBeNull();
  });
});
