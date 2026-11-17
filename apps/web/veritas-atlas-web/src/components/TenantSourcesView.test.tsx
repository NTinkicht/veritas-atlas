import { renderToStaticMarkup } from "react-dom/server";
import { describe, expect, it } from "vitest";
import { TenantSourcesListView, type SourcesView } from "./TenantSourcesView";

// SYNTHETIC fixtures only.
const src = { id: "s-1", canonicalKey: "example.test/synthetic", createdAtUtc: "2026-11-01T08:00:00Z" };
const render = (view: SourcesView, onViewAttempts?: () => void) =>
  renderToStaticMarkup(<TenantSourcesListView view={view} signInHref="/login?returnTo=%2Fsources" onViewAttempts={onViewAttempts} />);

describe("TenantSourcesListView", () => {
  it("shows loading and empty states", () => {
    expect(render({ kind: "loading" })).toContain("Loading sources");
    expect(render({ kind: "loaded", sources: [] })).toContain("No sources are registered");
  });

  it("offers sign-in only when unauthenticated", () => {
    const unauth = render({ kind: "error", failure: { kind: "unauthenticated", message: "Session ended." } });
    expect(unauth).toContain('href="/login?returnTo=%2Fsources"');
    const forbidden = render({ kind: "error", failure: { kind: "forbidden", message: "Not permitted." } });
    expect(forbidden).toContain('role="alert"');
    expect(forbidden).toContain("Not permitted.");
    expect(forbidden).not.toContain("Sign in");
  });

  it("lists sources and says the registry is not proof of verification", () => {
    const html = render({ kind: "loaded", sources: [src] });
    expect(html).toContain("Registered sources (1)");
    expect(html).toContain("example.test/synthetic");
    expect(html).toContain("not proof");
  });

  it("renders the attempts button only when a handler is given", () => {
    expect(render({ kind: "loaded", sources: [src] })).not.toContain("View attempts");
    expect(render({ kind: "loaded", sources: [src] }, () => undefined)).toContain("View attempts");
  });

  it("escapes server-provided keys", () => {
    const html = render({ kind: "loaded", sources: [{ ...src, canonicalKey: "<script>alert(1)</script>" }] });
    expect(html).not.toContain("<script>");
    expect(html).toContain("&lt;script&gt;");
  });
});
