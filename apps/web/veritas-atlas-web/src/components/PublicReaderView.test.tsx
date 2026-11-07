import { renderToStaticMarkup } from "react-dom/server";
import { describe, expect, it } from "vitest";
import { ERROR_MESSAGE, HIDDEN_MESSAGE, PublicReaderView } from "./PublicReaderView";

const ok = {
  kind: "ok" as const,
  publication: {
    id: "11111111-1111-4111-8111-111111111111",
    freshnessLabel: "CURRENT",
    citations: [
      { citationLabel: "S1", title: "<script>alert(1)</script>", quote: "<img src=x onerror=1>", sourceName: "Src", asOf: "2026-11-01T00:00:00Z", effectiveDate: null, freshnessLabel: "CURRENT" },
    ],
  },
};

describe("PublicReaderView", () => {
  it("renders citation, dates and freshness with escaped text", () => {
    const html = renderToStaticMarkup(<PublicReaderView state={ok} />);
    expect(html).toContain("S1");
    expect(html).toContain("As of 2026-11-01");
    expect(html).toContain("Effective not stated");
    expect(html).toContain("&lt;script&gt;");
    expect(html).not.toContain("<script>");
    expect(html).not.toContain("<img");
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
