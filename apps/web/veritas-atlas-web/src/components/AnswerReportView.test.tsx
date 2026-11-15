import { renderToStaticMarkup } from "react-dom/server";
import { describe, expect, it } from "vitest";
import { ReportResult, type ReportView } from "./AnswerReportView";

// SYNTHETIC fixtures only.
const html = (view: ReportView) => renderToStaticMarkup(<ReportResult view={view} signInHref="/login?returnTo=%2Fanswer-report" />);
const claim = { claimId: "c-1", text: "Register lists <b>X</b>", temporalScope: "current", historicalAsOf: null, nonCurrentEvidence: false, citations: [{ evidenceRef: "ev1:abc", freshness: "current" }] };

describe("ReportResult", () => {
  it("renders nothing when idle and a status when loading", () => {
    expect(html({ kind: "idle" })).toBe("");
    expect(html({ kind: "loading" })).toContain('role="status"');
  });
  it("shows the sign-in link only for an unauthenticated failure", () => {
    expect(html({ kind: "error", failure: { kind: "unauthenticated", message: "m" } })).toContain('href="/login?returnTo=%2Fanswer-report"');
    expect(html({ kind: "error", failure: { kind: "server", message: "m" } })).not.toContain("href=");
  });
  it("renders a served claim as text with citations and the caller-asserted label", () => {
    const out = html({ kind: "loaded", result: { decision: "serve", humanVerification: "Verified", claim } });
    expect(out).toContain("Register lists &lt;b&gt;X&lt;/b&gt;");
    expect(out).toContain("ev1:abc");
    expect(out).toContain("freshness: current");
    expect(out).toContain("asserted by the caller");
    expect(out).not.toContain("Historical evidence");
  });
  it("discloses the historical as-of date for non-current evidence", () => {
    const out = html({ kind: "loaded", result: { decision: "serve", humanVerification: "Verified", claim: { ...claim, nonCurrentEvidence: true, historicalAsOf: "2020-01-31" } } });
    expect(out).toContain("as of 2020-01-31");
  });
  it("shows only blocker codes, never claim text, for a blocked answer", () => {
    const out = html({ kind: "loaded", result: { decision: "block", blockers: [{ code: "human_verification_missing", claimId: "c-1", detail: null }] } });
    expect(out).toContain("Report blocked");
    expect(out).toContain("human_verification_missing");
    expect(out).not.toContain("Register lists");
  });
});
