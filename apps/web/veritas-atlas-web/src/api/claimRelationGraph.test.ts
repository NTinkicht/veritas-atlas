import { describe, expect, it } from "vitest";
import {
  InvalidGraphInput,
  MalformedGraphResponse,
  buildGraphBody,
  classifyGraphFailure,
  describeReason,
  emptyClaim,
  parseGraphResponse,
} from "./claimRelationGraph";

// SYNTHETIC fixtures only. The API serialises enums as integers.
const claim = (id: string, over = {}) => ({ ...emptyClaim(), claimId: id, evidenceRef: `ev1:${id.trim()}`, subject: "S", predicate: "p", ...over });
const node = (claimId: string, status: unknown) => ({ claimId, evidenceRef: `ev1:${claimId}`, sourceId: null, status });
const edge = (over = {}) => ({
  relation: 2, reason: "value_conflict_same_as_of", firstClaimId: "a", secondClaimId: "b",
  firstEvidenceRef: "ev1:a", secondEvidenceRef: "ev1:b", firstSourceId: "s1", secondSourceId: null,
  supersededClaimId: null, supersedingClaimId: null, ...over,
});
const ok = (nodes: unknown[], edges: unknown[]) => ({ evidenceVerified: false, graph: { tenantId: "t", nodes, edges } });

describe("relation graph request body", () => {
  it("trims, omits blanks and keeps negation", () => {
    const body = buildGraphBody([claim(" a ", { value: " 5 ", negated: true, asOf: "2024-01-31" }), claim("b")]);
    expect(body.claims[0]).toEqual({
      claimId: "a", evidenceRef: "ev1:a", sourceId: undefined, subject: "S", entityKey: undefined,
      predicate: "p", negated: true, value: "5", asOf: "2024-01-31",
    });
    expect(body.claims[1].asOf).toBeUndefined();
  });
  it("refuses bad input before any request", () => {
    expect(() => buildGraphBody([])).toThrow(InvalidGraphInput);
    expect(() => buildGraphBody([claim("a"), claim("a")])).toThrow(/used twice/);
    expect(() => buildGraphBody([claim("")])).toThrow(/id is required/);
    expect(() => buildGraphBody([claim("a", { subject: "" })])).toThrow(/subject is required/);
    expect(() => buildGraphBody([claim("a", { value: "x\ny" })])).toThrow(InvalidGraphInput);
    expect(() => buildGraphBody([claim("a", { value: "x".repeat(501) })])).toThrow(InvalidGraphInput);
    expect(() => buildGraphBody([claim("a", { asOf: "31/01/2024" })])).toThrow(/YYYY-MM-DD/);
    expect(() => buildGraphBody(Array.from({ length: 201 }, (_, i) => claim(`c${i}`)))).toThrow(InvalidGraphInput);
  });
});

describe("relation graph response", () => {
  it("maps integer enums to names", () => {
    const g = parseGraphResponse(ok([node("a", 0), node("b", 4)], [edge()]), ["b", "a"]);
    expect(g.nodes.map((n) => n.status)).toEqual(["conflicted", "isolated"]);
    expect(g.edges[0]).toMatchObject({ relation: "conflicting", firstSourceId: "s1", secondSourceId: null });
  });
  it("rejects unknown enum values, evidenceVerified true, a changed node set and unknown edge claims", () => {
    expect(() => parseGraphResponse(ok([node("a", 9)], []), ["a"])).toThrow(MalformedGraphResponse);
    expect(() => parseGraphResponse(ok([node("a", "weird")], []), ["a"])).toThrow(MalformedGraphResponse);
    expect(() => parseGraphResponse(ok([node("a", 4)], [edge({ relation: 0 })]), ["a"])).toThrow(MalformedGraphResponse);
    expect(() => parseGraphResponse({ ...ok([node("a", 4)], []), evidenceVerified: true }, ["a"])).toThrow(MalformedGraphResponse);
    expect(() => parseGraphResponse(ok([node("a", 4)], []), ["a", "b"])).toThrow(/node set/);
    expect(() => parseGraphResponse(ok([node("a", 4), node("b", 4)], [edge({ secondClaimId: "zzz" })]), ["a", "b"])).toThrow(/unknown claim/);
    expect(() => parseGraphResponse(null, ["a"])).toThrow(MalformedGraphResponse);
  });
});

describe("failures and reasons", () => {
  it("classifies each failure honestly", () => {
    expect(classifyGraphFailure(new InvalidGraphInput("x")).kind).toBe("invalid");
    expect(classifyGraphFailure({ status: 401 }).kind).toBe("unauthenticated");
    expect(classifyGraphFailure({ status: 403 }).kind).toBe("forbidden");
    expect(classifyGraphFailure({ status: 400 }).kind).toBe("bad-request");
    expect(classifyGraphFailure({ status: 503 }).message).toContain("HTTP 503");
    expect(classifyGraphFailure(new TypeError("net"), false).message).toContain("offline");
  });
  it("explains known reasons and shows unknown codes as a code", () => {
    expect(describeReason("entity_unresolved")).toContain("no resolved entity");
    expect(describeReason("new_code")).toBe("Reason code: new_code");
  });
});
