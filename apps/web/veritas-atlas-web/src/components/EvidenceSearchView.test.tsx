import { renderToStaticMarkup } from "react-dom/server";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MemoryRouter } from "react-router-dom";
import { describe, expect, it, vi } from "vitest";
import { isPresentedAsCurrent, type EvidenceHit } from "../api/evidenceSearch";
import { EvidenceSearchPage } from "../pages/EvidenceSearchPage";
import { CitationInspector, EvidenceResults, type SearchView } from "./EvidenceSearchView";

// SYNTHETIC fixtures only.
const base: EvidenceHit = {
  evidenceRef: "synthetic-ref-1",
  sourceUri: "https://example.test/synthetic/1",
  sourceEntryId: "11111111-1111-4111-8111-111111111111",
  jurisdiction: "gb-uk",
  sourceType: "statute",
  attestationState: "Verified",
  retrievedAtUtc: "2026-11-02T09:00:00Z",
  freshness: "CURRENT",
  freshnessEligible: true,
  freshnessReason: null,
  freshnessEvaluatedAt: "2026-11-02T11:00:00Z",
};

const noop = () => undefined;
const results = (view: SearchView) =>
  renderToStaticMarkup(<EvidenceResults view={view} selectedRef={null} onSelect={noop} onMore={noop} signInHref="/login?returnTo=%2Fevidence-search" />);
const loaded = (hits: EvidenceHit[], nextCursor: string | null = null): SearchView => ({
  kind: "loaded",
  hits,
  nextCursor,
  loadingMore: false,
  moreFailure: null,
});

describe("freshness is never presented as current unless proven", () => {
  it("styles only CURRENT + eligible as current", () => {
    expect(isPresentedAsCurrent(base)).toBe(true);
    expect(isPresentedAsCurrent({ ...base, freshnessEligible: false })).toBe(false);
    for (const freshness of ["STALE", "UNKNOWN", "SUPERSEDED"] as const) {
      expect(isPresentedAsCurrent({ ...base, freshness })).toBe(false);
    }
  });

  it("shows the label, as-of date and caution style next to every result", () => {
    const html = results(
      loaded([
        base,
        { ...base, evidenceRef: "r-unknown", freshness: "UNKNOWN", freshnessEligible: false },
        { ...base, evidenceRef: "r-stale", freshness: "STALE", freshnessEligible: false },
        { ...base, evidenceRef: "r-sup", freshness: "SUPERSEDED", freshnessEligible: false },
        { ...base, evidenceRef: "r-cur-no", freshness: "CURRENT", freshnessEligible: false },
      ]),
    );
    expect(html.match(/Retrieved 2026-11-02T09:00:00Z/g)).toHaveLength(5);
    expect(html.match(/es-fresh-current/g)).toHaveLength(1);
    expect(html.match(/es-fresh-caution/g)).toHaveLength(4);
    for (const label of ["Unknown", "Stale", "Superseded", "Current (not eligible)"]) expect(html).toContain(label);
  });
});

describe("states", () => {
  it("renders explicit idle, loading, empty and error states", () => {
    expect(results({ kind: "idle" })).toContain("Enter a query");
    expect(results({ kind: "loading" })).toContain('role="status"');
    expect(results(loaded([]))).toContain("No verified evidence matched");
    for (const kind of ["unauthenticated", "forbidden", "invalid", "offline", "server", "malformed"] as const) {
      const html = results({ kind: "error", failure: { kind, message: `msg-${kind}` } });
      expect(html).toContain('role="alert"');
      expect(html).toContain(`data-failure="${kind}"`);
      expect(html).toContain(`msg-${kind}`);
    }
  });

  it("offers sign-in only on 401 and never shows results on an error", () => {
    const html = results({ kind: "error", failure: { kind: "unauthenticated", message: "m" } });
    expect(html).toContain('href="/login?returnTo=%2Fevidence-search"');
    expect(results({ kind: "error", failure: { kind: "forbidden", message: "m" } })).not.toContain("<a ");
    expect(html).not.toContain("es-hit");
  });

  it("offers more results only with a next cursor", () => {
    expect(results(loaded([base], "c"))).toContain("Load more results");
    expect(results(loaded([base]))).not.toContain("Load more results");
  });
});

describe("source text is rendered as text, never HTML", () => {
  const hostile = '<img src=x onerror=alert(1)><script>alert(2)</script>';
  const evil = { ...base, evidenceRef: hostile, sourceUri: hostile, freshnessReason: hostile, jurisdiction: hostile };

  it("escapes hostile values in the list and the inspector", () => {
    const html = results(loaded([evil])) + renderToStaticMarkup(<CitationInspector hit={evil} />);
    expect(html).not.toContain("<img");
    expect(html).not.toContain("<script");
    expect(html).toContain("&lt;script&gt;");
  });

  it("does not turn the source URI into a link", () => {
    expect(renderToStaticMarkup(<CitationInspector hit={{ ...base, sourceUri: "javascript:alert(1)" }} />)).not.toContain("href=");
  });
});

describe("citation inspector", () => {
  it("shows every provenance field and is honest about the missing parser", () => {
    const html = renderToStaticMarkup(<CitationInspector hit={base} />);
    for (const text of [
      "Evidence reference",
      "https://example.test/synthetic/1",
      "Attestation state",
      "Verified",
      "Retrieved (UTC)",
      "2026-11-02T09:00:00Z",
      "Freshness evaluated (UTC)",
      "2026-11-02T11:00:00Z",
      "Parser name and version",
      "not provided by the search service",
    ]) {
      expect(html).toContain(text);
    }
    expect(html).toContain('aria-labelledby="es-inspector-title"');
  });

  it("prompts for a selection when nothing is selected", () => {
    expect(renderToStaticMarkup(<CitationInspector hit={null} />)).toContain("Select a result");
  });
});

describe("EvidenceSearchPage", () => {
  vi.stubGlobal("localStorage", { getItem: () => null, setItem: () => undefined, removeItem: () => undefined });
  const html = renderToStaticMarkup(
    <QueryClientProvider client={new QueryClient()}>
      <MemoryRouter initialEntries={["/evidence-search"]}>
        <EvidenceSearchPage />
      </MemoryRouter>
    </QueryClientProvider>,
  );

  it("starts empty with no data and no seeded results", () => {
    expect(html).toContain("Enter a query");
    expect(html).not.toContain("es-hit");
  });

  it("has a search landmark, labelled controls and a results region", () => {
    expect(html).toContain('role="search"');
    for (const id of ["es-q", "es-freshness", "es-jurisdiction", "es-type"]) {
      expect(html).toContain(`for="${id}"`);
      expect(html).toContain(`id="${id}"`);
    }
    expect(html).toContain('aria-labelledby="es-results-title"');
    expect(html).toContain("<main");
  });
});
