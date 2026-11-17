import { describe, expect, it } from "vitest";
import { apiOrigin, buildCsp, cspPlugin } from "./csp";

describe("production CSP", () => {
  it("never allows eval, inline scripts, wildcards or plugins", () => {
    const csp = buildCsp("https://api.example.org");
    expect(csp).not.toContain("unsafe-eval");
    expect(csp).not.toMatch(/script-src[^;]*unsafe-inline/);
    expect(csp).not.toMatch(/(^|[\s;])\*([\s;]|$)/);
    expect(csp).toContain("script-src 'self'");
    expect(csp).toContain("object-src 'none'");
    expect(csp).toContain("base-uri 'none'");
  });

  it("allows connections only to self and the configured API origin", () => {
    expect(buildCsp("https://api.example.org/base/path/")).toContain("connect-src 'self' https://api.example.org;");
    expect(buildCsp(undefined)).toContain("connect-src 'self';");
  });

  it("ignores unusable API bases instead of widening the policy", () => {
    for (const bad of ["", "not a url", "javascript:alert(1)", "ftp://x.example", "https://u:p@api.example.org"]) {
      expect(apiOrigin(bad)).toBeNull();
      expect(buildCsp(bad)).toContain("connect-src 'self';");
    }
  });

  it("injects exactly one CSP meta, on build only", () => {
    const plugin = cspPlugin("https://api.example.org");
    expect(plugin.apply).toBe("build");
    const tags = plugin.transformIndexHtml();
    expect(tags).toHaveLength(1);
    expect(tags[0].attrs["http-equiv"]).toBe("Content-Security-Policy");
    expect(tags[0].attrs.content).toBe(buildCsp("https://api.example.org"));
  });
});
