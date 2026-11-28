import { describe, expect, it } from "vitest";
import {
  MalformedCommentResponse,
  classifyCommentFailure,
  normalizeCommentBody,
  parseCommentList,
} from "./publicationComments";

// SYNTHETIC fixtures only.
const row = (extra: Record<string, unknown> = {}) => ({
  id: "11111111-1111-4111-8111-111111111111",
  revisionId: "22222222-2222-4222-8222-222222222222",
  clientKey: "33333333-3333-4333-8333-333333333333",
  author: "reviewer.one",
  body: "Please cite the 2024 amendment.",
  createdAtUtc: "2026-11-16T09:00:00Z",
  ...extra,
});

describe("parseCommentList", () => {
  it("maps the server shape in server order", () => {
    const list = parseCommentList({ comments: [row(), row({ id: "44444444-4444-4444-8444-444444444444", body: "Second" })] });
    expect(list.map((c) => c.body)).toEqual(["Please cite the 2024 amendment.", "Second"]);
    expect(list[0]).toMatchObject({ author: "reviewer.one", createdAtUtc: "2026-11-16T09:00:00Z" });
  });
  it("accepts an empty history", () => {
    expect(parseCommentList({ comments: [] })).toEqual([]);
  });
  it("rejects the whole list when one row is malformed", () => {
    for (const bad of [row({ body: 5 }), row({ author: "" }), row({ id: undefined }), row({ createdAtUtc: "not a date" })]) {
      expect(() => parseCommentList({ comments: [row(), bad] })).toThrow(MalformedCommentResponse);
    }
    expect(() => parseCommentList({})).toThrow(MalformedCommentResponse);
    expect(() => parseCommentList(null)).toThrow(MalformedCommentResponse);
  });
});

describe("normalizeCommentBody", () => {
  it("trims and accepts 1..2000 characters including line breaks and tabs", () => {
    expect(normalizeCommentBody("  ok  ")).toBe("ok");
    expect(normalizeCommentBody("a".repeat(2000))).toBe("a".repeat(2000));
    expect(normalizeCommentBody("line one\nline\ttwo")).toBe("line one\nline\ttwo");
  });
  it("refuses empty, over-long and control-character bodies", () => {
    for (const bad of ["", "   ", "a".repeat(2001), "bad\u0000body", "bell\u0007", "del\u007f"]) {
      expect(normalizeCommentBody(bad)).toBeNull();
    }
  });
});

describe("classifyCommentFailure", () => {
  it("distinguishes refusals without leaking existence", () => {
    expect(classifyCommentFailure({ status: 401 }).kind).toBe("unauthenticated");
    expect(classifyCommentFailure({ status: 403 }).kind).toBe("forbidden");
    expect(classifyCommentFailure({ status: 404 }).kind).toBe("not-found");
    expect(classifyCommentFailure({ status: 409, raw: { code: "comment_limit_reached" } }).kind).toBe("limit");
    expect(classifyCommentFailure({ status: 409, raw: { code: "comment_key_conflict" } }).kind).toBe("key-conflict");
    expect(classifyCommentFailure({ status: 400 }).kind).toBe("invalid");
    expect(classifyCommentFailure({ status: 500 }).kind).toBe("server");
    expect(classifyCommentFailure(new Error("x"), false).kind).toBe("offline");
    expect(classifyCommentFailure(new MalformedCommentResponse("x")).kind).toBe("malformed");
  });
});
