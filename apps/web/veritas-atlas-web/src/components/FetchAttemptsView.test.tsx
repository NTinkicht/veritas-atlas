import { renderToStaticMarkup } from "react-dom/server";
import { describe, expect, it } from "vitest";
import type { FetchAttempt } from "../api/tenantFetchAttempts";
import { FetchAttemptsPanel, type AttemptsView } from "./FetchAttemptsView";

// SYNTHETIC fixtures only.
const ok: FetchAttempt = { id: "a-1", requestedUri: "https://example.test/a", attemptedAtUtc: "2026-11-02T10:00:00Z", outcome: "succeeded", failureClass: null, httpStatus: 200 };
const bad: FetchAttempt = { id: "a-2", requestedUri: "https://example.test/b", attemptedAtUtc: "2026-11-02T11:00:00Z", outcome: "failed", failureClass: "http_status", httpStatus: 503 };
const render = (view: AttemptsView) => renderToStaticMarkup(<FetchAttemptsPanel view={view} signInHref="/login?returnTo=%2Fsources" />);

describe("FetchAttemptsPanel", () => {
  it("renders nothing when idle and a status while loading", () => {
    expect(render({ kind: "idle" })).toBe("");
    expect(render({ kind: "loading", sourceKey: "k" })).toContain("Loading fetch attempts");
  });

  it("offers sign-in only when unauthenticated", () => {
    expect(render({ kind: "error", sourceKey: "k", failure: { kind: "unauthenticated", message: "Ended." } })).toContain("Sign in");
    const server = render({ kind: "error", sourceKey: "k", failure: { kind: "server", message: "Failed." } });
    expect(server).toContain("Failed.");
    expect(server).not.toContain("Sign in");
  });

  it("shows an empty ledger honestly", () => {
    expect(render({ kind: "loaded", sourceKey: "k", attempts: [] })).toContain("No fetch attempts are recorded");
  });

  it("shows outcome, failure class and HTTP status, and omits them when absent", () => {
    const html = render({ kind: "loaded", sourceKey: "example.test", attempts: [ok, bad] });
    expect(html).toContain("(http_status)");
    expect(html).toContain("HTTP 503");
    expect(html).toContain("HTTP 200");
    expect(html).toContain("not proof");
    const okOnly = render({ kind: "loaded", sourceKey: "example.test", attempts: [{ ...ok, httpStatus: null }] });
    expect(okOnly).not.toContain("HTTP ");
    expect(okOnly).not.toContain("(http_status)");
  });

  it("escapes server-provided URIs", () => {
    const html = render({ kind: "loaded", sourceKey: "k", attempts: [{ ...ok, requestedUri: "<img src=x onerror=1>" }] });
    expect(html).not.toContain("<img");
    expect(html).toContain("&lt;img");
  });
});
