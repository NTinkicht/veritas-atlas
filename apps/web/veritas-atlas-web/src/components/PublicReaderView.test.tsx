import { renderToStaticMarkup } from "react-dom/server";
import { describe, expect, it } from "vitest";
import { ERROR_MESSAGE, HIDDEN_MESSAGE, PublicReaderView, REVIEW_STATEMENT } from "./PublicReaderView";

const ok = {
  kind: "ok" as const,
  publication: {
    id: "11111111-1111-4111-8111-111111111111",
    freshnessLabel: "CURRENT",
    humanReviewLabel: "independently-reviewed" as const,
    citations: [
      { citationLabel: "S1", title: "<script>alert(1)</script>", quote: "<img src=x onerror=1>", sourceName: "Src", asOf: "2026-11-01T00:00:00Z", effectiveDate: null, freshnessLabel: "CURRENT", contradictionDisclosure: null, uncertaintyLimit: null },
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
});
