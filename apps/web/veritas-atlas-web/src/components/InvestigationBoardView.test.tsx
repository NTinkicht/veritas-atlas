import { renderToStaticMarkup } from "react-dom/server";
import { describe, expect, it } from "vitest";
import type { CollectedEvidenceItem, Investigation } from "../api/investigations";
import { EvidenceBoard, InvestigationList } from "./InvestigationBoardView";

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
