import { renderToStaticMarkup } from "react-dom/server";
import { describe, expect, it } from "vitest";
import type { EvidenceDetail } from "../api/evidenceDetail";
import { EvidenceDetailBody, type DetailView } from "./EvidenceDetailView";

// SYNTHETIC fixtures only.
const detail: EvidenceDetail = {
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
  parserName: "uk-legislation-xml",
  parserVersion: "1.0.0",
  chunks: [{ ordinal: 1, elementId: "s1", text: "Synthetic verified text." }],
  chunksTruncated: false,
};
const render = (view: DetailView) => renderToStaticMarkup(<EvidenceDetailBody view={view} signInHref="/login?returnTo=%2Fevidence%2Fx" />);

describe("EvidenceDetailBody", () => {
  it("shows loading status", () => {
    expect(render({ kind: "loading" })).toContain("Loading verified evidence");
  });

  it("offers sign-in only for unauthenticated failures (positive control: it does for 401)", () => {
    const unauth = render({ kind: "error", failure: { kind: "unauthenticated", message: "Session ended." } });
    expect(unauth).toContain('role="alert"');
    expect(unauth).toContain('href="/login?returnTo=%2Fevidence%2Fx"');
    for (const kind of ["forbidden", "notfound", "offline", "server", "malformed"] as const) {
      const html = render({ kind: "error", failure: { kind, message: "Nope." } });
      expect(html).toContain('role="alert"');
      expect(html).toContain(`data-failure="${kind}"`);
      expect(html).not.toContain("Sign in");
    }
  });

  it("renders provenance, freshness and verified text when loaded", () => {
    const html = render({ kind: "loaded", detail });
    expect(html).toContain("synthetic-ref-1");
    expect(html).toContain("https://example.test/synthetic/1");
    expect(html).toContain("Synthetic verified text.");
    expect(html).toContain("uk-legislation-xml");
    expect(html).not.toContain("bounded portion");
  });

  it("shows the source URI as text, never as a link", () => {
    expect(render({ kind: "loaded", detail })).not.toContain("<a ");
  });

  it("does not present non-current evidence as eligible as current", () => {
    const html = render({
      kind: "loaded",
      detail: { ...detail, freshness: "STALE", freshnessEligible: false, freshnessReason: "max age exceeded" },
    });
    expect(html).toContain("STALE");
    expect(html).toContain("max age exceeded");
    expect(html).toMatch(/Eligible as current<\/dt><dd class="kv-value">no</);
  });

  it("states honest defaults for missing parser, reason and chunks", () => {
    const html = render({
      kind: "loaded",
      detail: { ...detail, parserName: null, parserVersion: null, freshnessReason: null, chunks: [] },
    });
    expect(html).toContain("not recorded");
    expect(html).toContain("none given");
    expect(html).toContain("No verified text chunks are recorded");
  });

  it("discloses truncation", () => {
    expect(render({ kind: "loaded", detail: { ...detail, chunksTruncated: true } })).toContain("bounded portion");
  });

  it("escapes server-provided markup", () => {
    const html = render({
      kind: "loaded",
      detail: { ...detail, chunks: [{ ordinal: 1, elementId: "s1", text: "<img src=x onerror=alert(1)>" }] },
    });
    expect(html).not.toContain("<img");
    expect(html).toContain("&lt;img");
  });
});
