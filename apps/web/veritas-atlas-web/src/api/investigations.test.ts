import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import {
  SHARE_MAX_GRANTEE_LENGTH,
  grantShare,
  isValidGrantee,
  listShares,
  parseShares,
  revokeShare,
  sharePath,
  sharesPath,
  MalformedInvestigationResponse,
  addAnnotation,
  draftAfterSave,
  annotationsPath,
  isValidAnnotationText,
  listAnnotations,
  parseAnnotation,
  parseAnnotations,
  bookmarkPath,
  bookmarksPath,
  parseBookmark,
  parseBookmarks,
  listBookmarks,
  putBookmark,
  removeBookmark,
  classifyInvestigationFailure,
  comparisonPath,
  evidenceItemsPath,
  parseEvidenceComparison,
  parseEvidenceItems,
  parseInvestigations,
  createInvestigation,
  transitionInvestigation,
  mergeCreated,
  isValidInvestigationTitle,
  collectEvidence,
  isValidEvidenceNote,
  isValidVerificationId,
} from "./investigations";

// SYNTHETIC fixtures only.
const ID = "11111111-1111-4111-8111-111111111111";
const EV = "22222222-2222-4222-8222-222222222222";
const inv = { id: ID, title: "Synthetic inquiry", createdBySubject: "analyst-1", createdAtUtc: "2026-11-02T09:00:00Z", state: "open" as const, version: 0 };
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

describe("bookmark client", () => {
  const bm = { investigationId: ID, evidenceItemId: EV, createdAtUtc: "2026-11-02T09:00:00Z" };
  beforeEach(() => vi.stubGlobal("localStorage", { getItem: () => null, setItem: () => undefined, removeItem: () => undefined }));
  afterEach(() => vi.unstubAllGlobals());

  it("builds GUID-validated paths", () => {
    expect(bookmarksPath(ID)).toBe(`/api/v1/tenant-investigations/${ID}/bookmarks`);
    expect(bookmarkPath(ID, EV)).toBe(`/api/v1/tenant-investigations/${ID}/evidence-items/${EV}/bookmark`);
    expect(() => bookmarksPath("../x")).toThrow(MalformedInvestigationResponse);
    expect(() => bookmarkPath(ID, "x/y")).toThrow(MalformedInvestigationResponse);
  });
  it("parses valid bookmarks and rejects a malformed list whole", () => {
    expect(parseBookmarks({ items: [bm] })).toEqual([bm]);
    expect(parseBookmark(bm)).toEqual(bm);
    expect(() => parseBookmarks({ items: [bm, { ...bm, evidenceItemId: "nope" }] })).toThrow(MalformedInvestigationResponse);
    expect(() => parseBookmarks({ items: [{ ...bm, createdAtUtc: "" }] })).toThrow(MalformedInvestigationResponse);
    expect(() => parseBookmarks({})).toThrow(MalformedInvestigationResponse);
    expect(() => parseBookmark(null)).toThrow(MalformedInvestigationResponse);
  });
  it("accepts a PUT answer for the requested item (positive control)", async () => {
    vi.stubGlobal("fetch", vi.fn(async () => new Response(JSON.stringify(bm), { status: 201 })));
    await expect(putBookmark(ID, EV)).resolves.toEqual(bm);
    const call = (fetch as unknown as ReturnType<typeof vi.fn>).mock.calls[0];
    expect(call[1].method).toBe("PUT");
  });
  it("rejects a PUT answer about a different item", async () => {
    const other = "44444444-4444-4444-8444-444444444444";
    vi.stubGlobal("fetch", vi.fn(async () => new Response(JSON.stringify({ ...bm, evidenceItemId: other }), { status: 200 })));
    await expect(putBookmark(ID, EV)).rejects.toBeInstanceOf(MalformedInvestigationResponse);
  });
  it("rejects a PUT answer about a different investigation", async () => {
    const otherInv = "55555555-5555-4555-8555-555555555555";
    vi.stubGlobal("fetch", vi.fn(async () => new Response(JSON.stringify({ ...bm, investigationId: otherInv }), { status: 200 })));
    await expect(putBookmark(ID, EV)).rejects.toBeInstanceOf(MalformedInvestigationResponse);
  });
  it("lists bookmarks with GET and rejects a malformed answer", async () => {
    const fetchMock = vi.fn(async () => new Response(JSON.stringify({ items: [bm] }), { status: 200 }));
    vi.stubGlobal("fetch", fetchMock);
    await expect(listBookmarks(ID)).resolves.toEqual([bm]);
    expect((fetchMock.mock.calls[0] as unknown as [string, RequestInit])[0]).toContain(`/${ID}/bookmarks`);
    vi.stubGlobal("fetch", vi.fn(async () => new Response(JSON.stringify({ items: [{ ...bm, evidenceItemId: "x" }] }), { status: 200 })));
    await expect(listBookmarks(ID)).rejects.toBeInstanceOf(MalformedInvestigationResponse);
  });
  it("removes a bookmark with DELETE and surfaces a refusal", async () => {
    const fetchMock = vi.fn(async () => new Response(null, { status: 204 }));
    vi.stubGlobal("fetch", fetchMock);
    await expect(removeBookmark(ID, EV)).resolves.toBeUndefined();
    const [url, init] = fetchMock.mock.calls[0] as unknown as [string, RequestInit];
    expect(init.method).toBe("DELETE");
    expect(url).toContain(`/${EV}/bookmark`);
    vi.stubGlobal("fetch", vi.fn(async () => new Response("{}", { status: 403 })));
    await expect(removeBookmark(ID, EV)).rejects.toMatchObject({ status: 403 });
  });
});

describe("annotation client", () => {
  const ITEM = "66666666-6666-4666-8666-666666666666";
  const an = { id: ID, investigationId: ID, evidenceItemId: ITEM, text: "Looks consistent", authorSubject: "analyst-1", createdAtUtc: "2026-11-02T09:00:00Z" };
  beforeEach(() => vi.stubGlobal("localStorage", { getItem: () => null, setItem: () => undefined, removeItem: () => undefined }));
  afterEach(() => vi.unstubAllGlobals());

  it("builds GUID-validated paths", () => {
    expect(annotationsPath(ID, ITEM)).toBe(`/api/v1/tenant-investigations/${ID}/evidence-items/${ITEM}/annotations`);
    expect(() => annotationsPath(ID, "x/y")).toThrow(MalformedInvestigationResponse);
    expect(() => annotationsPath("../x", ITEM)).toThrow(MalformedInvestigationResponse);
  });
  it("parses valid annotations and rejects a malformed list whole", () => {
    expect(parseAnnotations({ items: [an] })).toEqual([an]);
    expect(parseAnnotation(an)).toEqual(an);
    expect(() => parseAnnotations({ items: [an, { ...an, text: "" }] })).toThrow(MalformedInvestigationResponse);
    expect(() => parseAnnotations({ items: [{ ...an, evidenceItemId: "nope" }] })).toThrow(MalformedInvestigationResponse);
    expect(() => parseAnnotations({})).toThrow(MalformedInvestigationResponse);
    expect(() => parseAnnotation(null)).toThrow(MalformedInvestigationResponse);
  });
  it("mirrors the server text rule", () => {
    expect(isValidAnnotationText("ok")).toBe(true);
    expect(isValidAnnotationText("line one\nline two")).toBe(true);
    expect(isValidAnnotationText("x".repeat(1000))).toBe(true);
    expect(isValidAnnotationText("x".repeat(1001))).toBe(false);
    for (const bad of ["", "   ", " lead", "trail ", "tab\there", "nul\u0000", "bell\u0007", "c1\u0085x"]) {
      expect(isValidAnnotationText(bad)).toBe(false);
    }
  });
  it("lists annotations for the requested item (positive control)", async () => {
    vi.stubGlobal("fetch", vi.fn(async () => new Response(JSON.stringify({ items: [an] }), { status: 200 })));
    await expect(listAnnotations(ID, ITEM)).resolves.toEqual([an]);
  });
  it("rejects a list that mentions another item", async () => {
    const other = "77777777-7777-4777-8777-777777777777";
    vi.stubGlobal("fetch", vi.fn(async () => new Response(JSON.stringify({ items: [an, { ...an, evidenceItemId: other }] }), { status: 200 })));
    await expect(listAnnotations(ID, ITEM)).rejects.toBeInstanceOf(MalformedInvestigationResponse);
  });
  it("posts the text and accepts the matching answer", async () => {
    vi.stubGlobal("fetch", vi.fn(async () => new Response(JSON.stringify(an), { status: 201 })));
    await expect(addAnnotation(ID, ITEM, "Looks consistent")).resolves.toEqual(an);
    const call = (fetch as unknown as ReturnType<typeof vi.fn>).mock.calls[0];
    expect(call[1].method).toBe("POST");
    expect(JSON.parse(call[1].body)).toEqual({ text: "Looks consistent" });
  });
  it("rejects an answer about another item or with different text", async () => {
    const other = "77777777-7777-4777-8777-777777777777";
    vi.stubGlobal("fetch", vi.fn(async () => new Response(JSON.stringify({ ...an, evidenceItemId: other }), { status: 201 })));
    await expect(addAnnotation(ID, ITEM, "Looks consistent")).rejects.toBeInstanceOf(MalformedInvestigationResponse);
    vi.stubGlobal("fetch", vi.fn(async () => new Response(JSON.stringify({ ...an, text: "Something else" }), { status: 201 })));
    await expect(addAnnotation(ID, ITEM, "Looks consistent")).rejects.toBeInstanceOf(MalformedInvestigationResponse);
  });
  it("never sends invalid text", async () => {
    const fetchMock = vi.fn(async () => new Response(JSON.stringify(an), { status: 201 }));
    vi.stubGlobal("fetch", fetchMock);
    await expect(addAnnotation(ID, ITEM, " padded ")).rejects.toBeInstanceOf(MalformedInvestigationResponse);
    await expect(addAnnotation(ID, ITEM, "")).rejects.toBeInstanceOf(MalformedInvestigationResponse);
    expect(fetchMock).not.toHaveBeenCalled();
  });
});

describe("draftAfterSave", () => {
  it("clears the draft when it still equals the saved text", () => {
    expect(draftAfterSave("note", "note")).toBe("");
  });
  it("keeps text typed while the save was pending", () => {
    expect(draftAfterSave("note and more", "note")).toBe("note and more");
    expect(draftAfterSave("", "note")).toBe("");
  });
});

describe("create investigation client", () => {
  const created = { id: ID, creationKey: "key-1", title: "Synthetic inquiry", createdBySubject: "analyst-1", createdAtUtc: "2026-11-02T09:00:00Z", state: "open", version: 0 };
  beforeEach(() => vi.stubGlobal("localStorage", { getItem: () => null, setItem: () => undefined, removeItem: () => undefined }));
  afterEach(() => vi.unstubAllGlobals());

  it("mirrors the server title rule", () => {
    expect(isValidInvestigationTitle("Synthetic inquiry")).toBe(true);
    expect(isValidInvestigationTitle("x".repeat(200))).toBe(true);
    for (const bad of ["", "   ", " lead", "trail ", "x".repeat(201), "tab\there", "line\nbreak", "nul\u0000", "c1\u0085x"]) {
      expect(isValidInvestigationTitle(bad)).toBe(false);
    }
  });
  it("posts the title and creation key and returns the stored row (positive control)", async () => {
    const fetchMock = vi.fn(async () => new Response(JSON.stringify(created), { status: 201 }));
    vi.stubGlobal("fetch", fetchMock);
    const result = await createInvestigation("Synthetic inquiry", "key-1");
    expect(result).toEqual(inv);
    const [url, init] = fetchMock.mock.calls[0] as unknown as [string, RequestInit];
    expect(url).toContain("/api/v1/tenant-investigations");
    expect(init.method).toBe("POST");
    expect(JSON.parse(init.body as string)).toEqual({ creationKey: "key-1", title: "Synthetic inquiry" });
  });
  it("refuses an invalid title or key before any request", async () => {
    const fetchMock = vi.fn();
    vi.stubGlobal("fetch", fetchMock);
    await expect(createInvestigation(" padded", "key-1")).rejects.toBeInstanceOf(MalformedInvestigationResponse);
    await expect(createInvestigation("ok", "")).rejects.toBeInstanceOf(MalformedInvestigationResponse);
    expect(fetchMock).not.toHaveBeenCalled();
  });
  it("rejects an answer about a different investigation or a malformed body", async () => {
    vi.stubGlobal("fetch", vi.fn(async () => new Response(JSON.stringify({ ...created, title: "Other" }), { status: 200 })));
    await expect(createInvestigation("Synthetic inquiry", "key-1")).rejects.toBeInstanceOf(MalformedInvestigationResponse);
    vi.stubGlobal("fetch", vi.fn(async () => new Response(JSON.stringify({ ...created, id: "nope" }), { status: 200 })));
    await expect(createInvestigation("Synthetic inquiry", "key-1")).rejects.toBeInstanceOf(MalformedInvestigationResponse);
  });
  it("surfaces a server refusal instead of faking success", async () => {
    vi.stubGlobal("fetch", vi.fn(async () => new Response("{}", { status: 403 })));
    await expect(createInvestigation("Synthetic inquiry", "key-1")).rejects.toMatchObject({ status: 403 });
    vi.stubGlobal("fetch", vi.fn(async () => new Response("{}", { status: 409 })));
    await expect(createInvestigation("Synthetic inquiry", "key-1")).rejects.toMatchObject({ status: 409 });
  });
});

describe("mergeCreated", () => {
  const row = (id: string, title: string) => ({ id, title, createdBySubject: "analyst-1", createdAtUtc: "2026-11-02T09:00:00Z", state: "open" as const, version: 0 });
  const A = row("11111111-1111-4111-8111-111111111111", "A");
  const B = row("22222222-2222-4222-8222-222222222222", "B");
  it("keeps a row created before the initial list read finished, without duplicating it", () => {
    expect(mergeCreated([A], [B])).toEqual([A, B]);
    expect(mergeCreated([A], [B, A])).toEqual([A, B]);
    expect(mergeCreated([], [B])).toEqual([B]);
  });
});

describe("investigation state parsing", () => {
  it("rejects an unknown state or a bad version, whole list", () => {
    expect(() => parseInvestigations({ items: [{ ...inv, state: "archived" }] })).toThrow(MalformedInvestigationResponse);
    expect(() => parseInvestigations({ items: [{ ...inv, state: undefined }] })).toThrow(MalformedInvestigationResponse);
    for (const version of [-1, 1.5, "1", null, undefined]) {
      expect(() => parseInvestigations({ items: [{ ...inv, version }] })).toThrow(MalformedInvestigationResponse);
    }
    expect(parseInvestigations({ items: [{ ...inv, state: "closed", version: 3 }] })[0]).toMatchObject({ state: "closed", version: 3 });
  });
  it("classifies a 409 as a conflict", () => {
    expect(classifyInvestigationFailure({ status: 409 }).kind).toBe("conflict");
  });
});

describe("transitionInvestigation", () => {
  beforeEach(() => vi.stubGlobal("localStorage", { getItem: () => null, setItem: () => undefined, removeItem: () => undefined }));
  afterEach(() => vi.unstubAllGlobals());
  const reply = (body: unknown, status = 200) => vi.fn(async () => new Response(JSON.stringify(body), { status }));
  it("posts expectedVersion to /close and returns the closed row (positive control)", async () => {
    const fetchMock = reply({ ...inv, state: "closed", version: 1 });
    vi.stubGlobal("fetch", fetchMock);
    const result = await transitionInvestigation({ ...inv, version: 0 }, true);
    expect(result).toMatchObject({ state: "closed", version: 1 });
    const [url, init] = fetchMock.mock.calls[0] as unknown as [string, RequestInit];
    expect(url).toContain(`/api/v1/tenant-investigations/${ID}/close`);
    expect(init.method).toBe("POST");
    expect(JSON.parse(init.body as string)).toEqual({ expectedVersion: 0 });
  });
  it("posts to /reopen for a reopen", async () => {
    const fetchMock = reply({ ...inv, state: "open", version: 3 });
    vi.stubGlobal("fetch", fetchMock);
    await transitionInvestigation({ ...inv, state: "closed", version: 2 }, false);
    expect((fetchMock.mock.calls[0] as unknown as [string])[0]).toContain(`/${ID}/reopen`);
  });
  it("rejects an answer in the wrong state, for another investigation, or with an older version", async () => {
    vi.stubGlobal("fetch", reply({ ...inv, state: "open", version: 1 }));
    await expect(transitionInvestigation(inv, true)).rejects.toBeInstanceOf(MalformedInvestigationResponse);
    vi.stubGlobal("fetch", reply({ ...inv, id: "33333333-3333-4333-8333-333333333333", state: "closed", version: 1 }));
    await expect(transitionInvestigation(inv, true)).rejects.toBeInstanceOf(MalformedInvestigationResponse);
    vi.stubGlobal("fetch", reply({ ...inv, state: "closed", version: 4 }));
    await expect(transitionInvestigation({ ...inv, version: 5 }, true)).rejects.toBeInstanceOf(MalformedInvestigationResponse);
  });
  it("surfaces a 409 and refuses a non-GUID id before any request", async () => {
    vi.stubGlobal("fetch", reply({}, 409));
    await expect(transitionInvestigation(inv, true)).rejects.toMatchObject({ status: 409 });
    const fetchMock = vi.fn();
    vi.stubGlobal("fetch", fetchMock);
    await expect(transitionInvestigation({ ...inv, id: "nope" }, true)).rejects.toBeInstanceOf(MalformedInvestigationResponse);
    expect(fetchMock).not.toHaveBeenCalled();
  });
});

describe("share client", () => {
  const SH = { investigationId: ID, granteeSubject: "reviewer-1", grantedBySubject: "analyst-1", grantedAtUtc: "2026-11-02T09:00:00Z" };
  it("builds guarded, encoded paths", () => {
    expect(sharesPath(ID)).toBe(`/api/v1/tenant-investigations/${ID}/shares`);
    expect(sharePath(ID, "a b/c")).toBe(`/api/v1/tenant-investigations/${ID}/shares/a%20b%2Fc`);
    expect(() => sharesPath("../x")).toThrow(MalformedInvestigationResponse);
    expect(() => sharePath(ID, " bad")).toThrow(MalformedInvestigationResponse);
  });
  it("mirrors the server grantee rule", () => {
    expect(isValidGrantee("reviewer-1")).toBe(true);
    for (const bad of ["", " ", " x", "x ", "a\nb", "a\u0000b", "x".repeat(SHARE_MAX_GRANTEE_LENGTH + 1)]) expect(isValidGrantee(bad)).toBe(false);
    expect(isValidGrantee("x".repeat(SHARE_MAX_GRANTEE_LENGTH))).toBe(true);
  });
  it("rejects a malformed list whole", () => {
    expect(parseShares({ items: [SH] })).toHaveLength(1);
    expect(() => parseShares({ items: [{ ...SH, investigationId: "nope" }] })).toThrow(MalformedInvestigationResponse);
    expect(() => parseShares({})).toThrow(MalformedInvestigationResponse);
  });
  describe("over fetch", () => {
    beforeEach(() => vi.stubGlobal("localStorage", { getItem: () => null, setItem: () => undefined, removeItem: () => undefined }));
    afterEach(() => vi.unstubAllGlobals());
    const stub = (status: number, body?: unknown) =>
      vi.stubGlobal("fetch", vi.fn().mockResolvedValue(new Response(body === undefined ? null : JSON.stringify(body), { status })));
    it("refuses a list about another investigation", async () => {
      stub(200, { items: [{ ...SH, investigationId: "99999999-9999-4999-8999-999999999999" }] });
      await expect(listShares(ID)).rejects.toThrow(MalformedInvestigationResponse);
    });
    it("refuses a grant answered for another grantee and sends no request for an invalid one", async () => {
      stub(201, { ...SH, granteeSubject: "someone-else" });
      await expect(grantShare(ID, "reviewer-1")).rejects.toThrow(MalformedInvestigationResponse);
      const f = vi.fn();
      vi.stubGlobal("fetch", f);
      await expect(grantShare(ID, " bad")).rejects.toThrow(MalformedInvestigationResponse);
      expect(f).not.toHaveBeenCalled();
    });
    it("returns the stored row on a matching grant and surfaces a revoke refusal", async () => {
      stub(201, SH);
      await expect(grantShare(ID, "reviewer-1")).resolves.toEqual(SH);
      stub(404, { code: "tenant_investigation_not_found", message: "x" });
      await expect(revokeShare(ID, "reviewer-1")).rejects.toMatchObject({ status: 404 });
    });
  });
});

describe("collectEvidence", () => {
  beforeEach(() => vi.stubGlobal("localStorage", { getItem: () => null, setItem: () => undefined, removeItem: () => undefined }));
  afterEach(() => vi.unstubAllGlobals());
  const ITEM = "33333333-3333-4333-8333-333333333333";
  const row = { id: ITEM, investigationId: ID, evidenceVerificationId: EV, note: "why", collectedBySubject: "analyst-1", createdAtUtc: "2026-11-02T09:00:00Z" };
  const reply = (body: unknown, status = 201) => vi.fn(async () => new Response(JSON.stringify(body), { status }));
  it("posts the verification id and note to the evidence-items path (positive control)", async () => {
    const fetchMock = reply(row);
    vi.stubGlobal("fetch", fetchMock);
    const result = await collectEvidence(ID, EV, "why");
    expect(result).toEqual({ id: ITEM, evidenceVerificationId: EV, note: "why", collectedBySubject: "analyst-1", createdAtUtc: row.createdAtUtc });
    const [url, init] = fetchMock.mock.calls[0] as unknown as [string, RequestInit];
    expect(url).toContain(`/api/v1/tenant-investigations/${ID}/evidence-items`);
    expect(init.method).toBe("POST");
    expect(JSON.parse(init.body as string)).toEqual({ evidenceVerificationId: EV, note: "why" });
  });
  it("omits an empty note", async () => {
    const fetchMock = reply({ ...row, note: null });
    vi.stubGlobal("fetch", fetchMock);
    await collectEvidence(ID, EV, "");
    expect(JSON.parse((fetchMock.mock.calls[0] as unknown as [string, RequestInit])[1].body as string)).toEqual({ evidenceVerificationId: EV });
  });
  it("never reaches the network for an invalid id or note", async () => {
    const fetchMock = vi.fn();
    vi.stubGlobal("fetch", fetchMock);
    await expect(collectEvidence(ID, "nope", "")).rejects.toBeInstanceOf(MalformedInvestigationResponse);
    await expect(collectEvidence(ID, "00000000-0000-0000-0000-000000000000", "")).rejects.toBeInstanceOf(MalformedInvestigationResponse);
    await expect(collectEvidence(ID, EV, " padded")).rejects.toBeInstanceOf(MalformedInvestigationResponse);
    await expect(collectEvidence("nope", EV, "")).rejects.toBeInstanceOf(MalformedInvestigationResponse);
    expect(fetchMock).not.toHaveBeenCalled();
  });
  it("rejects an answer about another investigation or another verification id", async () => {
    vi.stubGlobal("fetch", reply({ ...row, investigationId: "44444444-4444-4444-8444-444444444444" }));
    await expect(collectEvidence(ID, EV, "why")).rejects.toBeInstanceOf(MalformedInvestigationResponse);
    vi.stubGlobal("fetch", reply({ ...row, evidenceVerificationId: "44444444-4444-4444-8444-444444444444" }));
    await expect(collectEvidence(ID, EV, "why")).rejects.toBeInstanceOf(MalformedInvestigationResponse);
    vi.stubGlobal("fetch", reply({ ...row, id: "nope" }));
    await expect(collectEvidence(ID, EV, "why")).rejects.toBeInstanceOf(MalformedInvestigationResponse);
  });
  it("surfaces a refusal status unchanged", async () => {
    vi.stubGlobal("fetch", reply({}, 409));
    await expect(collectEvidence(ID, EV, "")).rejects.toMatchObject({ status: 409 });
  });
  it("mirrors the server validators", () => {
    expect(isValidVerificationId(EV)).toBe(true);
    expect(isValidVerificationId("00000000-0000-0000-0000-000000000000")).toBe(false);
    expect(isValidEvidenceNote("")).toBe(true);
    expect(isValidEvidenceNote("a".repeat(500))).toBe(true);
    expect(isValidEvidenceNote("a".repeat(501))).toBe(false);
    expect(isValidEvidenceNote("trailing ")).toBe(false);
    expect(isValidEvidenceNote("tab\there")).toBe(false);
  });
});
