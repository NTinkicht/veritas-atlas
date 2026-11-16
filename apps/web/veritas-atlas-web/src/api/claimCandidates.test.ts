// @vitest-environment jsdom
import { afterEach, describe, expect, it, vi } from "vitest";
import {
  InvalidCandidatesInput, MalformedCandidatesResponse, buildCandidatesUrl, classifyCandidatesFailure,
  listClaimCandidates, parseCandidatesResponse,
} from "./claimCandidates";

// SYNTHETIC fixtures only.
const item = (over: Record<string, unknown> = {}) => ({
  claimKey: "k1", quotation: "The register lists X.", elementId: "e1", start: 3, length: 21,
  chunkTextSha256: "ab".repeat(32), status: "unconfirmed", answerable: false, ...over,
});
const body = (over: Record<string, unknown> = {}) => ({
  evidenceRef: "ev1:abc", extractorName: "claim-extractor", extractorVersion: "1",
  freshness: { state: "current", eligible: true, reason: null, evaluatedAt: null }, claims: [item()], ...over,
});

afterEach(() => vi.unstubAllGlobals());

describe("buildCandidatesUrl", () => {
  it("encodes the trimmed reference", () => {
    expect(buildCandidatesUrl(" ev1:a b&c ")).toBe("/api/v1/tenant-evidence/claim-candidates?evidenceRef=ev1%3Aa%20b%26c");
  });
  it.each(["", "   ", "x".repeat(513), "ev1:\u0000a"])("rejects %j without a request", (v) => {
    expect(() => buildCandidatesUrl(v)).toThrow(InvalidCandidatesInput);
  });
});

describe("parseCandidatesResponse", () => {
  it("accepts a well-formed response", () => {
    const r = parseCandidatesResponse(body(), "ev1:abc");
    expect(r.claims).toHaveLength(1);
    expect(r.claims[0].status).toBe("unconfirmed");
  });
  it("accepts more than 500 candidates (the server caps per chunk, not per evidence item)", () => {
    const claims = Array.from({ length: 501 }, (_, i) => item({ claimKey: `k${i}` }));
    expect(parseCandidatesResponse(body({ claims }), "ev1:abc").claims).toHaveLength(501);
  });
  it("accepts an empty candidate list", () => {
    expect(parseCandidatesResponse(body({ claims: [] }), "ev1:abc").claims).toEqual([]);
  });
  it.each([
    ["answerable true", body({ claims: [item({ answerable: true })] })],
    ["answerable missing", body({ claims: [item({ answerable: undefined })] })],
    ["status confirmed", body({ claims: [item({ status: "confirmed" })] })],
    ["empty quotation", body({ claims: [item({ quotation: "" })] })],
    ["zero-length span", body({ claims: [item({ length: 0 })] })],
    ["negative start", body({ claims: [item({ start: -1 })] })],
    ["foreign evidenceRef", body({ evidenceRef: "ev1:other" })],
    ["claims not an array", body({ claims: null })],
    ["freshness missing", body({ freshness: null })],
  ])("rejects the whole response: %s", (_name, raw) => {
    expect(() => parseCandidatesResponse(raw, "ev1:abc")).toThrow(MalformedCandidatesResponse);
  });
});

describe("classifyCandidatesFailure", () => {
  it.each([[401, "unauthenticated"], [403, "forbidden"], [404, "not-found"], [409, "refused"], [400, "bad-request"], [500, "server"]])(
    "maps HTTP %i to %s",
    (status, kind) => expect(classifyCandidatesFailure({ status, message: "SECRET server text" }).kind).toBe(kind),
  );
  it("never echoes server text and distinguishes offline", () => {
    expect(classifyCandidatesFailure({ status: 404, message: "SECRET server text" }).message).not.toContain("SECRET");
    expect(classifyCandidatesFailure(new Error("x"), false).message).toContain("offline");
    expect(classifyCandidatesFailure(new Error("x"), true).kind).toBe("offline");
  });
});

describe("listClaimCandidates", () => {
  it("issues one authenticated GET and parses the result", async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(JSON.stringify(body()), { status: 200, headers: { "Content-Type": "application/json" } }));
    vi.stubGlobal("fetch", fetchMock);
    const r = await listClaimCandidates(" ev1:abc ");
    expect(r.claims).toHaveLength(1);
    expect(fetchMock).toHaveBeenCalledTimes(1);
    const [url, init] = fetchMock.mock.calls[0];
    expect(String(url)).toContain("/api/v1/tenant-evidence/claim-candidates?evidenceRef=ev1%3Aabc");
    expect(init.method).toBe("GET");
  });
  it("makes no request for invalid input", async () => {
    const fetchMock = vi.fn();
    vi.stubGlobal("fetch", fetchMock);
    await expect(listClaimCandidates("")).rejects.toThrow(InvalidCandidatesInput);
    expect(fetchMock).not.toHaveBeenCalled();
  });
  it("rejects a response that is answerable", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(new Response(JSON.stringify(body({ claims: [item({ answerable: true })] })), { status: 200, headers: { "Content-Type": "application/json" } })));
    await expect(listClaimCandidates("ev1:abc")).rejects.toThrow(MalformedCandidatesResponse);
  });
});
