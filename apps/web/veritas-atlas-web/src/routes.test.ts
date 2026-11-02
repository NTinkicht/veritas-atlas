import { readFileSync } from "node:fs";
import { resolve } from "node:path";
import { describe, expect, it } from "vitest";
import { ROUTE_AUDIT, isMounted } from "./routeAudit";
import { buildRoutes } from "./routes";

const routesSource = readFileSync(resolve(__dirname, "routes.tsx"), "utf8");
const auditDoc = readFileSync(
  resolve(__dirname, "../../../../docs/architecture/WEB_SURFACE_AUDIT.md"),
  "utf8",
);
const registered = [...routesSource.matchAll(/path: "([^"]+)"/g)].map((m) => m[1]);

describe("web surface audit", () => {
  it("registers at least one route", () => {
    expect(registered.length).toBeGreaterThan(0);
  });

  it("has an audit entry for every registered route", () => {
    expect(registered.filter((p) => !(p in ROUTE_AUDIT))).toEqual([]);
  });

  it("has no audit entry for a route that is not registered", () => {
    expect(Object.keys(ROUTE_AUDIT).filter((p) => !registered.includes(p))).toEqual([]);
  });

  it("lists every route with its verdict in WEB_SURFACE_AUDIT.md", () => {
    const missing = registered.filter(
      (p) => !auditDoc.includes(`| \`${p}\` | ${ROUTE_AUDIT[p].page} | ${ROUTE_AUDIT[p].verdict} |`),
    );
    expect(missing).toEqual([]);
  });

  it("mounts only REAL routes in a production build", () => {
    const paths = buildRoutes(false).map((r) => r.path ?? "");
    expect(paths.length).toBeGreaterThan(0);
    const notReal = paths.filter((p) => ROUTE_AUDIT[p]?.verdict !== "REAL");
    expect(notReal).toEqual([]);
    expect(paths).not.toContain("/operational-actions");
    expect(paths).not.toContain("/persistence");
  });

  it("mounts every registered route in Development", () => {
    expect(buildRoutes(true).map((r) => r.path)).toEqual(registered);
  });

  it("never mounts an unaudited path", () => {
    expect(isMounted("/not-audited", true)).toBe(false);
    expect(isMounted("/not-audited", false)).toBe(false);
  });

  it("requires sign-in for every production route except /login", () => {
    for (const route of buildRoutes(false)) {
      if (route.path === "/login") continue;
      const el = route.element as { type: unknown } | undefined;
      expect(el, route.path).toBeDefined();
      expect((el!.type as { name?: string }).name, route.path).toBe("RequireAuth");
    }
  });

  it("does not fetch or render the seeded scenario snapshot outside Development", () => {
    const src = readFileSync(resolve(__dirname, "pages/DashboardPage.tsx"), "utf8");
    expect(src).toContain("const SHOW_SEEDED_SNAPSHOT = import.meta.env.DEV;");
    expect(src).toContain("SHOW_SEEDED_SNAPSHOT ? getScenarioSnapshot() : Promise.resolve(null)");
    expect(src.match(/\{SHOW_SEEDED_SNAPSHOT && \(/g)?.length).toBe(2);
  });
});
