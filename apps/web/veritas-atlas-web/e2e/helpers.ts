import type { Page } from "@playwright/test";

/**
 * Returns descriptions of everything that overflows the viewport horizontally: the document itself
 * scrolling sideways, or any rendered element whose box extends past the right edge / before the left edge.
 * An empty array means no horizontal overflow or clipping.
 */
export async function findHorizontalOverflow(page: Page): Promise<string[]> {
  return page.evaluate(() => {
    const vw = document.documentElement.clientWidth;
    const found: string[] = [];
    if (document.documentElement.scrollWidth > vw) {
      found.push(`document scrollWidth ${document.documentElement.scrollWidth} > viewport ${vw}`);
    }
    for (const el of Array.from(document.body.querySelectorAll("*"))) {
      const rect = el.getBoundingClientRect();
      if (rect.width === 0 || rect.height === 0) continue;
      if (rect.right > vw + 0.5 || rect.left < -0.5) {
        const id = el.id ? `#${el.id}` : "";
        found.push(`${el.tagName.toLowerCase()}${id} spans ${Math.round(rect.left)}..${Math.round(rect.right)} of ${vw}`);
      }
    }
    return found;
  });
}

export const PUBLICATION_ID = "0b1c2d3e-4f50-4a6b-8c7d-9e0f1a2b3c4d";
export const PUBLICATION_URL_GLOB = "**/api/v1/public/publications/**";

export const publicationFixture = {
  id: PUBLICATION_ID,
  freshnessLabel: "CURRENT",
  humanReviewLabel: "independently-reviewed",
  citations: [
    {
      citationLabel: "C-1",
      title: "Statement on the regional budget",
      quote: "A deliberately long quoted passage used to confirm that wrapped text stays inside narrow viewports without clipping.",
      sourceName: "Official gazette",
      asOf: "2026-03-01",
      effectiveDate: "2026-03-01",
      freshnessLabel: "CURRENT",
      contradictionDisclosure: null,
      uncertaintyLimit: "Figures are provisional.",
      sourceUri: "https://example.org/gazette/2026/03",
      confidence: "Medium",
    },
  ],
};
