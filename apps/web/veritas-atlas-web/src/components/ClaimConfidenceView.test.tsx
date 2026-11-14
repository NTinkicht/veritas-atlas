import { renderToStaticMarkup } from "react-dom/server";
import { describe, expect, it } from "vitest";
import { ConfidenceResult } from "./ClaimConfidenceView";

// SYNTHETIC fixtures only.
const show = (view: Parameters<typeof ConfidenceResult>[0]["view"]) =>
  renderToStaticMarkup(<ConfidenceResult view={view} signInHref="/login?returnTo=%2Fclaim-confidence" />);

describe("ConfidenceResult", () => {
  it("shows nothing before a request, so no band is invented", () => {
    expect(show({ kind: "idle" })).toBe("");
  });
  it("shows the server band, reasons and the not-truth disclaimer, escaping API text", () => {
    const html = show({ kind: "loaded", label: { claimId: "<i>c</i>", band: "low", reasons: ["conflicting_evidence", "x<b>"] } });
    expect(html).toContain("Evidence confidence: low");
    expect(html).toContain("Conflicting evidence exists.");
    expect(html).toContain("&lt;i&gt;c&lt;/i&gt;");
    expect(html).not.toContain("<i>c");
    expect(html).toContain("not a judgement that the claim is true");
  });
  it("shows only an alert with sign-in on 401, never a band", () => {
    const html = show({ kind: "error", failure: { kind: "unauthenticated", message: "Your session has ended." } });
    expect(html).toContain('role="alert"');
    expect(html).toContain('href="/login?returnTo=%2Fclaim-confidence"');
    expect(html).not.toContain("Evidence confidence");
  });
});
