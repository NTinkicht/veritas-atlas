// @vitest-environment jsdom
import { cleanup, render, screen } from "@testing-library/react";
import { afterEach, describe, expect, it } from "vitest";
import type { ClassificationResult } from "../api/statementClassification";
import { ClassificationResultView } from "./StatementClassificationView";

const result = (over: Partial<ClassificationResult> = {}): ClassificationResult => ({
  evidenceRef: "ev1:abc", classifierName: "statement-classifier", classifierVersion: "1", statementClass: "inference",
  supportingEvidenceRef: null, span: null, freshness: { state: "current", eligible: true, reason: null, evaluatedAt: null }, ...over,
});
afterEach(cleanup);

describe("ClassificationResultView", () => {
  it("renders nothing when idle and a status while loading", () => {
    const { container } = render(<ClassificationResultView view={{ kind: "idle" }} signInHref="/login" />);
    expect(container.innerHTML).toBe("");
    cleanup();
    render(<ClassificationResultView view={{ kind: "loading" }} signInHref="/login" />);
    expect(screen.getByRole("status")).toBeTruthy();
  });
  it("shows the class, states it is not a claim and shows no span for an inference", () => {
    render(<ClassificationResultView view={{ kind: "loaded", result: result() }} signInHref="/login" />);
    expect(screen.getByText(/Inference:/)).toBeTruthy();
    expect(screen.getByRole("note").textContent).toContain("not a claim");
    expect(screen.queryByText(/Matched element/)).toBeNull();
  });
  it("shows the span of a quotation and flags non-current freshness", () => {
    render(<ClassificationResultView view={{ kind: "loaded", result: result({ statementClass: "quotation", span: { elementId: "s1", start: 3, length: 12 }, freshness: { state: "stale", eligible: false, reason: "age-window-exceeded", evaluatedAt: null } }) }} signInHref="/login" />);
    expect(screen.getByText(/characters 3 to 15/)).toBeTruthy();
    expect(screen.getByText(/not eligible as current-state evidence/)).toBeTruthy();
  });
  it("offers sign-in only for an unauthenticated failure", () => {
    render(<ClassificationResultView view={{ kind: "error", failure: { kind: "unauthenticated", message: "ended" } }} signInHref="/login?x" />);
    expect(screen.getByRole("link", { name: "Sign in" }).getAttribute("href")).toBe("/login?x");
    cleanup();
    render(<ClassificationResultView view={{ kind: "error", failure: { kind: "not-found", message: "none" } }} signInHref="/login" />);
    expect(screen.queryByRole("link")).toBeNull();
  });
  it("renders hostile API text as text, not markup", () => {
    render(<ClassificationResultView view={{ kind: "loaded", result: result({ supportingEvidenceRef: "<img src=x onerror=alert(1)>" }) }} signInHref="/login" />);
    expect(document.querySelector("img")).toBeNull();
  });
});
