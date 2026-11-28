import { describe, expect, it } from "vitest";
import {
  MalformedOrganizerResponse,
  classifyOrganizerFailure,
  normalizeFolderName,
  normalizeTag,
  parseFolder,
  parseTagList,
} from "./investigationOrganizer";

// SYNTHETIC fixtures only.
const tagRow = (tag: string) => ({ investigationId: "11111111-1111-4111-8111-111111111111", tag, createdAtUtc: "2026-11-02T09:00:00Z" });

describe("normalizeTag", () => {
  it("lowercases like the server and accepts the documented alphabet", () => {
    expect(normalizeTag("Planning-2026")).toBe("planning-2026");
    expect(normalizeTag("a")).toBe("a");
    expect(normalizeTag("a".repeat(40))).toBe("a".repeat(40));
  });
  it("refuses empty, over-long, leading punctuation, spaces and slashes", () => {
    for (const bad of ["", "a".repeat(41), "-x", "_x", ".x", "a b", "a/b", "a%2Fb", " a", "é"]) {
      expect(normalizeTag(bad), JSON.stringify(bad)).toBeNull();
    }
  });
});

describe("normalizeFolderName", () => {
  it("trims and accepts letters, digits, single inner spaces, underscore, dot and dash", () => {
    expect(normalizeFolderName("  Case files 2026 ")).toBe("Case files 2026");
    expect(normalizeFolderName("a_b.c-d")).toBe("a_b.c-d");
  });
  it("refuses empty, over-long, double spaces, other characters and control characters", () => {
    for (const bad of ["", "   ", "a".repeat(61), "a  b", "a/b", "a\tb", "a\nb", "é"]) {
      expect(normalizeFolderName(bad), JSON.stringify(bad)).toBeNull();
    }
    expect(normalizeFolderName("a".repeat(60))).toBe("a".repeat(60));
  });
});

describe("parseTagList / parseFolder", () => {
  it("maps the server shape", () => {
    expect(parseTagList({ items: [tagRow("alpha"), tagRow("beta")] })).toEqual(["alpha", "beta"]);
    expect(parseFolder({ investigationId: "x", name: "Case files", updatedAtUtc: "z" })).toBe("Case files");
  });
  it("fails closed on a malformed list, one bad row rejecting all", () => {
    expect(() => parseTagList({})).toThrow(MalformedOrganizerResponse);
    expect(() => parseTagList(null)).toThrow(MalformedOrganizerResponse);
    expect(() => parseTagList({ items: [tagRow("ok"), { tag: 3 }] })).toThrow(MalformedOrganizerResponse);
    expect(() => parseTagList({ items: [tagRow("Upper")] })).toThrow(MalformedOrganizerResponse);
    expect(() => parseFolder({})).toThrow(MalformedOrganizerResponse);
    expect(() => parseFolder({ name: "bad/name" })).toThrow(MalformedOrganizerResponse);
  });
});

describe("classifyOrganizerFailure", () => {
  const fail = (status: number, code?: string) => classifyOrganizerFailure({ status, raw: code ? { code } : undefined });
  it("maps each documented refusal", () => {
    expect(fail(401).kind).toBe("unauthenticated");
    expect(fail(403).kind).toBe("forbidden");
    expect(fail(404).kind).toBe("not-found");
    expect(fail(409, "investigation_closed").kind).toBe("closed");
    expect(fail(409, "tag_limit_reached").kind).toBe("limit");
    expect(fail(400).kind).toBe("invalid");
    expect(fail(500).kind).toBe("server");
  });
  it("distinguishes offline from a server failure and flags a malformed response", () => {
    expect(classifyOrganizerFailure(new TypeError("fetch failed"), true).kind).toBe("offline");
    expect(classifyOrganizerFailure(new TypeError("fetch failed"), false).message).toContain("offline");
    expect(classifyOrganizerFailure(new MalformedOrganizerResponse("x")).kind).toBe("malformed");
  });
});
