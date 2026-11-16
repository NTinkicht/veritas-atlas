// @vitest-environment jsdom
import { cleanup, fireEvent, render, screen } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MemoryRouter } from "react-router-dom";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { InvalidClassifyInput, type ClassificationResult } from "../api/statementClassification";
import { StatementClassificationPage } from "./StatementClassificationPage";

// SYNTHETIC fixtures only. The network-facing client is mocked; rendering and failure mapping are real.
const classify = vi.hoisted(() => vi.fn());
vi.mock("../api/statementClassification", async (importOriginal) => ({
  ...(await importOriginal<typeof import("../api/statementClassification")>()),
  classifyStatement: classify,
}));

const result = (statementClass: ClassificationResult["statementClass"]): ClassificationResult => ({
  evidenceRef: "ev1:abc", classifierName: "statement-classifier", classifierVersion: "1", statementClass,
  supportingEvidenceRef: null, span: null, freshness: { state: "current", eligible: true, reason: null, evaluatedAt: null },
});
type Deferred = { promise: Promise<ClassificationResult>; resolve: (v: ClassificationResult) => void };
function deferred(): Deferred {
  let resolve!: Deferred["resolve"];
  const promise = new Promise<ClassificationResult>((res) => {
    resolve = res;
  });
  return { promise, resolve };
}

const setup = () =>
  render(
    <QueryClientProvider client={new QueryClient()}>
      <MemoryRouter initialEntries={["/statement-classification?x=1"]}>
        <StatementClassificationPage />
      </MemoryRouter>
    </QueryClientProvider>,
  );
const fill = () => {
  fireEvent.change(screen.getByLabelText(/Evidence reference/), { target: { value: "ev1:abc" } });
  fireEvent.change(screen.getByLabelText(/Statement/), { target: { value: "The register lists X." } });
};
const submit = () => fireEvent.click(screen.getByRole("button", { name: "Classify statement" }));

beforeEach(() => classify.mockReset());
afterEach(cleanup);

describe("StatementClassificationPage", () => {
  it("makes no request before submission", () => {
    setup();
    expect(screen.queryByRole("heading", { name: "Statement classification", level: 2 })).toBeNull();
    expect(classify).not.toHaveBeenCalled();
  });

  it("submits the entered values and shows the class", async () => {
    classify.mockResolvedValue(result("unsupported"));
    setup();
    fill();
    submit();
    expect(classify).toHaveBeenCalledWith({ evidenceRef: "ev1:abc", statement: "The register lists X." });
    expect(await screen.findByText(/Unsupported:/)).toBeTruthy();
  });

  it("shows invalid input as an alert", async () => {
    classify.mockImplementationOnce(() => Promise.reject(new InvalidClassifyInput("Enter a statement of 1-2000 characters with no control characters.")));
    setup();
    submit();
    expect((await screen.findByRole("alert")).textContent).toContain("Enter a statement");
  });

  it("links to sign-in with a return path on 401 and never echoes the statement on 404", async () => {
    classify.mockImplementationOnce(() => Promise.reject({ status: 401 }));
    setup();
    fill();
    submit();
    const link = await screen.findByRole("link", { name: "Sign in" });
    expect(link.getAttribute("href")).toBe(`/login?returnTo=${encodeURIComponent("/statement-classification?x=1")}`);
    classify.mockImplementationOnce(() => Promise.reject({ status: 404 }));
    submit();
    const alert = await screen.findByText(/No verified evidence with that reference/);
    expect(alert.textContent).not.toContain("The register lists X.");
    expect(screen.queryByRole("link", { name: "Sign in" })).toBeNull();
  });

  it("maps a 409 to the integrity message and shows no classification", async () => {
    classify.mockImplementationOnce(() => Promise.reject({ status: 409 }));
    setup();
    fill();
    submit();
    expect(await screen.findByText(/failed integrity checks/)).toBeTruthy();
    expect(screen.queryByText(/Quotation:|Inference:|Unresolved:|Unsupported:/)).toBeNull();
  });

  it("shows only the latest request's result", async () => {
    const first = deferred();
    const second = deferred();
    classify.mockReturnValueOnce(first.promise).mockReturnValueOnce(second.promise);
    setup();
    fill();
    submit();
    submit();
    second.resolve(result("inference"));
    expect(await screen.findByText(/Inference:/)).toBeTruthy();
    first.resolve(result("unsupported"));
    await Promise.resolve();
    expect(screen.queryByText(/Unsupported:/)).toBeNull();
    expect(screen.getByText(/Inference:/)).toBeTruthy();
  });
});
