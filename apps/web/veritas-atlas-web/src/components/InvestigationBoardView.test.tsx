import { renderToStaticMarkup } from "react-dom/server";
import { describe, expect, it } from "vitest";
import type { CollectedEvidenceItem, Investigation } from "../api/investigations";
import { EvidenceBoard, EvidenceComparison, InvestigationList } from "./InvestigationBoardView";

// SYNTHETIC fixtures only.
const inv: Investigation = { id: "11111111-1111-4111-8111-111111111111", title: "Synthetic <b>inquiry</b>", createdBySubject: "analyst-1", createdAtUtc: "2026-11-02T09:00:00Z" };
const item: CollectedEvidenceItem = { id: "33333333-3333-4333-8333-333333333333", evidenceVerificationId: "22222222-2222-4222-8222-222222222222", note: "<script>alert(1)</script>", collectedBySubject: "analyst-1", createdAtUtc: "2026-11-02T09:00:00Z" };
const noop = () => undefined;
const list = (view: Parameters<typeof InvestigationList>[0]["view"]) =>
  renderToStaticMarkup(<InvestigationList view={view} selectedId={null} onSelect={noop} signInHref="/login?returnTo=%2Finvestigations" />);

describe("InvestigationList", () => {
  it("shows an honest empty state, not sample rows", () => {
    expect(list({ kind: "loaded", data: [] })).toContain("No investigations are visible to you yet.");
  });
  it("escapes API text instead of rendering HTML", () => {
    const html = list({ kind: "loaded", data: [inv] });
    expect(html).toContain("Synthetic &lt;b&gt;inquiry&lt;/b&gt;");
    expect(html).not.toContain("<b>inquiry");
  });
  it("offers sign-in on 401 and shows no rows", () => {
    const html = list({ kind: "error", failure: { kind: "unauthenticated", message: "Your session has ended." } });
    expect(html).toContain('role="alert"');
    expect(html).toContain('href="/login?returnTo=%2Finvestigations"');
    expect(html).not.toContain("<li");
  });
});

describe("EvidenceBoard", () => {
  const board = (view: Parameters<typeof EvidenceBoard>[0]["view"]) =>
    renderToStaticMarkup(<EvidenceBoard investigation={inv} view={view} signInHref="/login" />);
  it("escapes notes and shows the verified evidence id", () => {
    const html = board({ kind: "loaded", data: [item] });
    expect(html).toContain("&lt;script&gt;");
    expect(html).not.toContain("<script>");
    expect(html).toContain(item.evidenceVerificationId);
  });
  it("shows empty and error states", () => {
    expect(board({ kind: "loaded", data: [] })).toContain("No evidence has been collected");
    expect(board({ kind: "error", failure: { kind: "not-found", message: "That investigation was not found." } })).toContain("not found");
  });
});

describe("EvidenceComparison and compare controls", () => {
  const side = (itemId: string, over: Record<string, unknown> = {}) => ({
    itemId,
    evidenceVerificationId: "22222222-2222-4222-8222-222222222222",
    note: "<img src=x onerror=alert(1)>",
    parentEvidenceRef: "ev1:aaa",
    rawRetrievalId: "11111111-1111-4111-8111-111111111111",
    attestationId: "11111111-1111-4111-8111-111111111111",
    parseRunId: "11111111-1111-4111-8111-111111111111",
    verifiedAtUtc: "2026-11-02T09:00:00Z",
    ...over,
  });
  const data = {
    left: side("33333333-3333-4333-8333-333333333333"),
    right: side("44444444-4444-4444-8444-444444444444", { parentEvidenceRef: "ev1:bbb", note: null }),
    sameEvidenceRef: false,
    sameRawRetrieval: true,
    sameAttestation: true,
    sameParseRun: true,
  };
  const render = (view: Parameters<typeof EvidenceComparison>[0]["view"]) =>
    renderToStaticMarkup(<EvidenceComparison view={view} signInHref="/login" />);
  it("shows both sides, honest same/different flags and escapes notes", () => {
    const html = render({ kind: "loaded", data });
    expect(html).toContain("Evidence reference: Different");
    expect(html).toContain("Raw retrieval: Same");
    expect(html).toContain("ev1:aaa");
    expect(html).toContain("ev1:bbb");
    expect(html).toContain("No note");
    expect(html).toContain("&lt;img");
    expect(html).not.toContain("<img");
  });
  it("shows failures and loading without any comparison data", () => {
    const err = render({ kind: "error", failure: { kind: "not-found", message: "That investigation was not found." } });
    expect(err).toContain('role="alert"');
    expect(err).not.toContain("Evidence reference");
    expect(render({ kind: "loading" })).toContain("Loading comparison");
  });
  it("renders compare checkboxes only when a handler is given and disables a third pick", () => {
    const second = { ...item, id: "44444444-4444-4444-8444-444444444444" };
    const third = { ...item, id: "55555555-5555-4555-8555-555555555555" };
    const view = { kind: "loaded" as const, data: [item, second, third] };
    const without = renderToStaticMarkup(<EvidenceBoard investigation={inv} view={view} signInHref="/login" />);
    expect(without).not.toContain("Compare this item");
    const html = renderToStaticMarkup(
      <EvidenceBoard investigation={inv} view={view} signInHref="/login" pickedIds={[item.id, second.id]} onTogglePick={noop} />,
    );
    expect((html.match(/Compare this item/g) ?? []).length).toBe(3);
    expect((html.match(/disabled=""/g) ?? []).length).toBe(1);
  });
});

describe("bookmark controls", () => {
  const render = (props: Partial<Parameters<typeof EvidenceBoard>[0]>) =>
    renderToStaticMarkup(
      <EvidenceBoard investigation={inv} view={{ kind: "loaded", data: [item] }} signInHref="/login" {...props} />,
    );
  it("hides bookmark controls when no handler is supplied", () => {
    expect(render({})).not.toContain("Bookmark");
  });
  it("offers Bookmark for an unbookmarked item and Remove bookmark for a bookmarked one", () => {
    const off = render({ bookmarkedIds: [], onToggleBookmark: noop });
    expect(off).toContain(">Bookmark<");
    expect(off).not.toContain("Remove bookmark");
    const on = render({ bookmarkedIds: [item.id], onToggleBookmark: noop });
    expect(on).toContain("Remove bookmark");
    expect(on).toContain('aria-pressed="true"');
  });
  it("disables the control while a request is in flight", () => {
    expect(render({ bookmarkedIds: [], onToggleBookmark: noop, busyIds: [item.id] })).toContain("disabled");
    expect(render({ bookmarkedIds: [], onToggleBookmark: noop, busyIds: [] })).not.toContain("disabled");
  });
  it("shows a bookmark failure as an alert without hiding the evidence", () => {
    const html = render({
      bookmarkedIds: [],
      onToggleBookmark: noop,
      bookmarkNotice: { kind: "forbidden", message: "Not permitted." },
    });
    expect(html).toContain('role="alert"');
    expect(html).toContain("Not permitted.");
    expect(html).toContain(item.evidenceVerificationId);
  });
});
