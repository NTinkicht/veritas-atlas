import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import {
  MalformedRevisionResponse,
  buildTransitionBody,
  classifyRevisionFailure,
  isRevisionId,
  parseRevision,
  parseTransitionResponse,
  revisionPath,
  transitionPath,
  transitionRevision,
} from "./publicationRevisions";

// SYNTHETIC fixtures only.
const ID = "11111111-1111-4111-8111-111111111111";
const D = "a".repeat(64);
const rev = { id: ID, authorId: "analyst-1", contentDigest: D, approvedDigest: null, policyPermitsPublicDisplay: true, state: "Submitted", version: 2, factsVersion: 0 };

beforeEach(() => vi.stubGlobal("localStorage", { getItem: (k: string) => (k === "veritas_atlas_access_token" ? "synthetic-token" : null) }));
afterEach(() => vi.unstubAllGlobals());

describe("revision parsing", () => {
  it("parses a valid revision and defaults factsVersion", () => {
    expect(parseRevision(rev)).toEqual(rev);
    const rest: Record<string, unknown> = { ...rev };
    delete rest.factsVersion;
    expect(parseRevision(rest).factsVersion).toBe(0);
  });
  it("rejects unknown state, bad guid, bad digest, bad version, missing flag", () => {
    for (const bad of [
      { ...rev, state: "Live" }, { ...rev, id: "x" }, { ...rev, contentDigest: "abc" },
      { ...rev, approvedDigest: "ZZ" }, { ...rev, version: 1.5 }, { ...rev, version: -1 },
      { ...rev, policyPermitsPublicDisplay: "yes" }, { ...rev, authorId: "" }, null, "text",
    ]) {
      expect(() => parseRevision(bad)).toThrow(MalformedRevisionResponse);
    }
  });
  it("rejects a transition response without a boolean replayed flag", () => {
    expect(parseTransitionResponse({ replayed: false, revision: rev }).revision.state).toBe("Submitted");
    expect(() => parseTransitionResponse({ revision: rev })).toThrow(MalformedRevisionResponse);
    expect(() => parseTransitionResponse({ replayed: true, revision: { ...rev, state: "?" } })).toThrow(MalformedRevisionResponse);
  });
});

describe("request construction", () => {
  it("builds paths only for GUID ids", () => {
    expect(isRevisionId(ID)).toBe(true);
    expect(revisionPath(ID)).toBe(`/api/v1/tenant-publication-revisions/${ID}`);
    expect(transitionPath(ID)).toBe(`/api/v1/tenant-publication-revisions/${ID}/transitions`);
    expect(() => revisionPath("../../x")).toThrow(MalformedRevisionResponse);
  });
  it("builds the exact transition body and refuses invalid input", () => {
    expect(buildTransitionBody("Approve", 3, "op-1")).toEqual({ action: "Approve", expectedVersion: 3, operationId: "op-1" });
    expect(() => buildTransitionBody("Delete" as never, 3, "op")).toThrow(MalformedRevisionResponse);
    expect(() => buildTransitionBody("Approve", -1, "op")).toThrow(MalformedRevisionResponse);
    expect(() => buildTransitionBody("Approve", 1, "")).toThrow(MalformedRevisionResponse);
  });
  it("posts the body to the transitions path and parses the reply", async () => {
    const fetchMock = vi.fn().mockResolvedValue({ ok: true, json: async () => ({ replayed: false, revision: { ...rev, state: "Approved", version: 3 } }) });
    vi.stubGlobal("fetch", fetchMock);
    const result = await transitionRevision(ID, "Approve", 2, "op-9");
    expect(result.revision.state).toBe("Approved");
    const [url, init] = fetchMock.mock.calls[0];
    expect(init.headers.Authorization).toBe("Bearer synthetic-token");
    expect(String(url)).toContain(`/api/v1/tenant-publication-revisions/${ID}/transitions`);
    expect(init.method).toBe("POST");
    expect(JSON.parse(init.body)).toEqual({ action: "Approve", expectedVersion: 2, operationId: "op-9" });
  });
  it("propagates a refused transition instead of reporting success", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue({ ok: false, status: 403, json: async () => ({ message: "no" }) }));
    await expect(transitionRevision(ID, "Approve", 2, "op-9")).rejects.toMatchObject({ status: 403 });
  });
});

describe("failure classification", () => {
  it("maps statuses honestly", () => {
    expect(classifyRevisionFailure({ status: 401 }).kind).toBe("unauthenticated");
    expect(classifyRevisionFailure({ status: 403 }).kind).toBe("forbidden");
    expect(classifyRevisionFailure({ status: 404 }).kind).toBe("not-found");
    expect(classifyRevisionFailure({ status: 409 }).kind).toBe("conflict");
    expect(classifyRevisionFailure({ status: 500 }).kind).toBe("server");
    expect(classifyRevisionFailure(new MalformedRevisionResponse("x")).kind).toBe("malformed");
    expect(classifyRevisionFailure(new TypeError("fail"), false).message).toContain("offline");
  });
});
