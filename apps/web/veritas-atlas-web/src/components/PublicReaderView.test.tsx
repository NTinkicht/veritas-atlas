import { renderToStaticMarkup } from "react-dom/server";
import { describe, expect, it } from "vitest";
import { ERROR_MESSAGE, HIDDEN_MESSAGE, NON_CURRENT_NOTICE, PublicReaderView, REVIEW_STATEMENT } from "./PublicReaderView";

const ok = {
  kind: "ok" as const,
  publication: {
    id: "11111111-1111-4111-8111-111111111111",
    freshnessLabel: "CURRENT",
    humanReviewLabel: "independently-reviewed" as const,
    citations: [
      { citationLabel: "S1", title: "<script>alert(1)</script>", quote: "<img src=x onerror=1>", sourceName: "Src", asOf: "2026-11-01T00:00:00Z", effectiveDate: null, freshnessLabel: "CURRENT", contradictionDisclosure: null, uncertaintyLimit: null, sourceUri: null },
    ],
  },
};

describe("PublicReaderView", () => {
  it("states that a human reviewed the publication", () => {
    expect(renderToStaticMarkup(<PublicReaderView state={ok} />)).toContain(REVIEW_STATEMENT);
    expect(renderToStaticMarkup(<PublicReaderView state={{ kind: "hidden" }} />)).not.toContain(REVIEW_STATEMENT);
  });

  it("renders citation, dates and freshness with escaped text", () => {
    const html = renderToStaticMarkup(<PublicReaderView state={ok} />);
    expect(html).toContain("S1");
    expect(html).toContain("As of 2026-11-01");
    expect(html).toContain("Effective not stated");
    expect(html).toContain("&lt;script&gt;");
    expect(html).not.toContain("<script>");
    expect(html).not.toContain("<img");
  });

  it("renders disclosures as escaped text only when present", () => {
    const none = renderToStaticMarkup(<PublicReaderView state={ok} />);
    expect(none).not.toContain("Contradiction disclosure");
    expect(none).not.toContain("Uncertainty limit");
    const c = { ...ok.publication.citations[0], contradictionDisclosure: "Conflicts <b>with</b> 2025", uncertaintyLimit: "Single source" };
    const html = renderToStaticMarkup(<PublicReaderView state={{ kind: "ok", publication: { ...ok.publication, citations: [c] } }} />);
    expect(html).toContain("Contradiction disclosure: Conflicts &lt;b&gt;with&lt;/b&gt; 2025");
    expect(html).toContain("Uncertainty limit: Single source");
    expect(html).not.toContain("<b>");
  });

  it("links the source only when the citation carries a safe https provenance link", () => {
    const withLink = { ...ok.publication.citations[0], sourceUri: "https://gazette.example.org/notice/1?a=1&b=2" };
    const html = renderToStaticMarkup(<PublicReaderView state={{ kind: "ok", publication: { ...ok.publication, citations: [withLink] } }} />);
    expect(html).toContain('href="https://gazette.example.org/notice/1?a=1&amp;b=2"');
    expect(html).toContain('rel="noopener noreferrer nofollow"');
    expect(html).toContain('target="_blank"');
    expect(html).not.toMatch(/login|dashboard|\/investigations|\/publication-revisions/i);
    // Positive-control counterpart: without a link there is no anchor at all.
    expect(renderToStaticMarkup(<PublicReaderView state={ok} />)).not.toContain("<a ");
  });

  it("keeps the source-local calendar date for non-UTC offsets", () => {
    const c = { ...ok.publication.citations[0], asOf: "2026-11-01T00:00:00+03:00", effectiveDate: "2026-11-02T23:30:00-05:00" };
    const html = renderToStaticMarkup(<PublicReaderView state={{ kind: "ok", publication: { ...ok.publication, citations: [c] } }} />);
    expect(html).toContain("As of 2026-11-01");
    expect(html).toContain("Effective 2026-11-02");
  });

  it("shows identical text for every hidden cause and no staff controls", () => {
    const hidden = renderToStaticMarkup(<PublicReaderView state={{ kind: "hidden" }} />);
    expect(hidden).toContain(HIDDEN_MESSAGE);
    for (const html of [hidden, renderToStaticMarkup(<PublicReaderView state={ok} />), renderToStaticMarkup(<PublicReaderView state={{ kind: "error" }} />)]) {
      expect(html).not.toMatch(/<a |<button|<form|<input|href=|login|dashboard/i);
    }
    expect(renderToStaticMarkup(<PublicReaderView state={{ kind: "error" }} />)).toContain(ERROR_MESSAGE);
  });

  it("discloses non-current citations with the historical as-of date", () => {
    for (const label of ["STALE", "SUPERSEDED", "UNKNOWN", "bogus"]) {
      const c = { ...ok.publication.citations[0], freshnessLabel: label };
      const html = renderToStaticMarkup(<PublicReaderView state={{ kind: "ok", publication: { ...ok.publication, citations: [c] } }} />);
      expect(html).toContain(NON_CURRENT_NOTICE);
      expect(html).toContain("Historical as of 2026-11-01");
    }
    const undated = { ...ok.publication.citations[0], freshnessLabel: "STALE", asOf: null };
    expect(renderToStaticMarkup(<PublicReaderView state={{ kind: "ok", publication: { ...ok.publication, citations: [undated] } }} />)).toContain("Historical as of not stated");
  });

  it("shows no non-current notice for CURRENT evidence (positive control)", () => {
    expect(renderToStaticMarkup(<PublicReaderView state={ok} />)).not.toContain(NON_CURRENT_NOTICE);
    const c = { ...ok.publication.citations[0], freshnessLabel: " current " };
    expect(renderToStaticMarkup(<PublicReaderView state={{ kind: "ok", publication: { ...ok.publication, citations: [c] } }} />)).not.toContain(NON_CURRENT_NOTICE);
  });

  it("discloses a non-current publication-level label and keeps hidden/error free of notices", () => {
    expect(renderToStaticMarkup(<PublicReaderView state={{ kind: "ok", publication: { ...ok.publication, freshnessLabel: "STALE" } }} />)).toContain(NON_CURRENT_NOTICE);
    for (const state of [{ kind: "hidden" as const }, { kind: "error" as const }]) {
      expect(renderToStaticMarkup(<PublicReaderView state={state} />)).not.toContain(NON_CURRENT_NOTICE);
    }
  });
});
