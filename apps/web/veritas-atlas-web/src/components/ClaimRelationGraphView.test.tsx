import { renderToStaticMarkup } from "react-dom/server";
import { describe, expect, it } from "vitest";
import { GraphResult } from "./ClaimRelationGraphView";
import type { GraphEdge, RelationGraph } from "../api/claimRelationGraph";

// SYNTHETIC fixtures only.
const show = (view: Parameters<typeof GraphResult>[0]["view"]) =>
  renderToStaticMarkup(<GraphResult view={view} signInHref="/login?returnTo=%2Fclaim-relations" />);
const edge = (over: Partial<GraphEdge>): GraphEdge => ({
  relation: "conflicting", reason: "value_conflict_same_as_of", firstClaimId: "a", secondClaimId: "b",
  firstEvidenceRef: "ev1:a", secondEvidenceRef: "ev1:b", firstSourceId: "s1", secondSourceId: null,
  supersededClaimId: null, supersedingClaimId: null, ...over,
});
const graph = (edges: GraphEdge[]): RelationGraph => ({
  nodes: [{ claimId: "a", evidenceRef: "ev1:a", sourceId: "s1", status: "conflicted" }, { claimId: "b", evidenceRef: "ev1:b", sourceId: null, status: "isolated" }],
  edges,
});

describe("GraphResult", () => {
  it("shows nothing before a request", () => {
    expect(show({ kind: "idle" })).toBe("");
  });
  it.each([
    ["agreeing", "Agreeing"],
    ["conflicting", "Conflicting"],
    ["superseded", "Superseded"],
    ["unresolved", "Unresolved"],
  ] as const)("labels a %s relation in text with attribution", (relation, label) => {
    const html = show({ kind: "loaded", graph: graph([edge({ relation })]) });
    expect(html).toContain(`<strong>${label}</strong>`);
    expect(html).toContain("evidence ev1:a, source s1");
    expect(html).toContain("not verified");
  });
  it("names the superseded claim and handles no relations", () => {
    expect(show({ kind: "loaded", graph: graph([edge({ relation: "superseded", supersededClaimId: "a", supersedingClaimId: "b" })]) })).toContain("a is superseded by b");
    expect(show({ kind: "loaded", graph: graph([]) })).toContain("No related claims were found.");
  });
  it("escapes API text", () => {
    const html = show({ kind: "loaded", graph: graph([edge({ firstClaimId: "<i>x</i>" })]) });
    expect(html).not.toContain("<i>x");
    expect(html).toContain("&lt;i&gt;x&lt;/i&gt;");
  });
  it("shows only an alert with sign-in on 401, never relations", () => {
    const html = show({ kind: "error", failure: { kind: "unauthenticated", message: "Your session has ended." } });
    expect(html).toContain('role="alert"');
    expect(html).toContain('href="/login?returnTo=%2Fclaim-relations"');
    expect(html).not.toContain("Relations (");
  });
});
