import { renderToStaticMarkup } from "react-dom/server";
import { describe, expect, it } from "vitest";
import { CitationResult } from "./CitationInspectorView";

// SYNTHETIC fixtures only.
const show = (view: Parameters<typeof CitationResult>[0]["view"]) =>
  renderToStaticMarkup(<CitationResult view={view} signInHref="/login?returnTo=%2Fcitation-inspector" />);

describe("CitationResult", () => {
  it("shows nothing before a request, so no verdict is invented", () => {
    expect(show({ kind: "idle" })).toBe("");
  });
  it("shows a rejected verdict with plain-language codes, freshness and historical context, escaping API text", () => {
    const html = show({
      kind: "loaded",
      verdict: {
        valid: false,
        code: "EVIDENCE_STALE",
        items: [{ evidenceRef: "<b>ev1:x</b>", valid: false, code: "EVIDENCE_STALE", freshness: "STALE", evaluatedAt: "2026-01-01T00:00:00Z", historicalAsOf: "2020-05-01" }],
      },
    });
    expect(html).toContain("Citations rejected");
    expect(html).toContain("The cited evidence is stale for a current-state claim.");
    expect(html).toContain("&lt;b&gt;ev1:x&lt;/b&gt;");
    expect(html).not.toContain("<b>ev1");
    expect(html).toContain("Freshness: STALE");
    expect(html).toContain("Historical as of 2020-05-01");
    expect(html).toContain("Nothing was repaired");
  });
  it("shows accepted only from a valid server verdict", () => {
    const html = show({ kind: "loaded", verdict: { valid: true, code: null, items: [{ evidenceRef: "ev1:ok", valid: true, code: null, freshness: "CURRENT", evaluatedAt: null, historicalAsOf: null }] } });
    expect(html).toContain("Citations accepted");
    expect(html).not.toContain("Citations rejected");
  });
  it("shows errors honestly with a sign-in link only when unauthenticated", () => {
    expect(show({ kind: "error", failure: { kind: "unauthenticated", message: "ended" } })).toContain("/login?returnTo=");
    expect(show({ kind: "error", failure: { kind: "server", message: "boom" } })).not.toContain("Sign in");
  });
});
