import { describe, expect, it } from "vitest";
import {
  MalformedSavedSearchResponse,
  classifySavedSearchFailure,
  isValidSavedSearchName,
  parseSavedSearchList,
} from "./savedSearches";

// SYNTHETIC fixtures only.
const row = (extra: Record<string, unknown> = {}) => ({
  id: "11111111-1111-4111-8111-111111111111",
  name: "Recent acts",
  query: "planning",
  sourceEntryId: null,
  jurisdiction: "UK",
  sourceType: null,
  freshness: "Current",
  parserName: null,
  parserVersion: null,
  subjectTerm: null,
  createdAtUtc: "2026-11-02T09:00:00Z",
  version: 1,
  ...extra,
});

describe("parseSavedSearchList", () => {
  it("maps the server shape and the freshness enum name", () => {
    const [s] = parseSavedSearchList({ items: [row()] });
    expect(s).toMatchObject({ name: "Recent acts", query: "planning", freshness: "CURRENT", jurisdiction: "UK", hasUnsupportedCriteria: false });
  });

  it("flags criteria the page cannot express so they are never silently dropped", () => {
    for (const extra of [{ sourceEntryId: "22222222-2222-4222-8222-222222222222" }, { parserName: "p" }, { parserVersion: "1" }, { subjectTerm: "x" }, { freshness: "Bogus" }]) {
      expect(parseSavedSearchList({ items: [row(extra)] })[0].hasUnsupportedCriteria).toBe(true);
    }
  });

  it("rejects the whole list when one row is malformed", () => {
    expect(() => parseSavedSearchList({ items: [row(), row({ name: 5 })] })).toThrow(MalformedSavedSearchResponse);
    expect(() => parseSavedSearchList({ items: [row({ version: "1" })] })).toThrow(MalformedSavedSearchResponse);
    expect(() => parseSavedSearchList({ items: [row({ id: undefined })] })).toThrow(MalformedSavedSearchResponse);
    expect(() => parseSavedSearchList({})).toThrow(MalformedSavedSearchResponse);
    expect(() => parseSavedSearchList(null)).toThrow(MalformedSavedSearchResponse);
  });
});

describe("isValidSavedSearchName", () => {
  it("accepts 1..100 trimmed characters without control characters", () => {
    expect(isValidSavedSearchName("ok")).toBe(true);
    expect(isValidSavedSearchName("a".repeat(100))).toBe(true);
    for (const bad of ["", " pad", "pad ", "a".repeat(101), "bad\nname"]) expect(isValidSavedSearchName(bad)).toBe(false);
  });
});

describe("classifySavedSearchFailure", () => {
  it("distinguishes refusals", () => {
    expect(classifySavedSearchFailure({ status: 401 }).kind).toBe("unauthenticated");
    expect(classifySavedSearchFailure({ status: 403 }).kind).toBe("forbidden");
    expect(classifySavedSearchFailure({ status: 409, raw: { code: "saved_search_name_taken" } }).kind).toBe("name-taken");
    expect(classifySavedSearchFailure({ status: 409, raw: { code: "saved_search_limit_reached" } }).kind).toBe("limit");
    expect(classifySavedSearchFailure({ status: 400 }).kind).toBe("invalid");
    expect(classifySavedSearchFailure({ status: 500 }).kind).toBe("server");
    expect(classifySavedSearchFailure(new Error("x"), false).kind).toBe("offline");
  });
});
