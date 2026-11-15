// @vitest-environment jsdom
import { act, cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MemoryRouter } from "react-router-dom";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { InvalidComposeInput, type ComposeResult } from "../api/answerReport";
import { AnswerReportPage } from "./AnswerReportPage";

// SYNTHETIC fixtures only. The network-facing composer is mocked; classification and rendering are real.
const compose = vi.hoisted(() => vi.fn());
vi.mock("../api/answerReport", async (importOriginal) => ({
  ...(await importOriginal<typeof import("../api/answerReport")>()),
  composeReport: compose,
}));

const claim = { claimId: "c-1", text: "Register lists X", temporalScope: "current", historicalAsOf: null, nonCurrentEvidence: false, citations: [{ evidenceRef: "ev1:abc", freshness: "current" }] };
const served = (text: string): ComposeResult => ({ decision: "serve", humanVerification: "Verified", claim: { ...claim, text } });

type Deferred = { promise: Promise<ComposeResult>; resolve: (v: ComposeResult) => void };
function deferred(): Deferred {
  let resolve!: Deferred["resolve"];
  const promise = new Promise<ComposeResult>((res) => {
    resolve = res;
  });
  return { promise, resolve };
}

const setup = () =>
  render(
    <QueryClientProvider client={new QueryClient()}>
      <MemoryRouter initialEntries={["/answer-report?x=1"]}>
        <AnswerReportPage />
      </MemoryRouter>
    </QueryClientProvider>,
  );
const submit = () => fireEvent.click(screen.getByRole("button", { name: "Compose report" }));

beforeEach(() => compose.mockReset());
afterEach(cleanup);

describe("AnswerReportPage", () => {
  it("renders the form with no result and no request before submission", () => {
    setup();
    expect(screen.getByLabelText(/Claim id/)).toBeTruthy();
    expect(screen.queryByText("Source-backed report", { selector: "h2" })).toBeNull();
    expect(compose).not.toHaveBeenCalled();
  });

  it("submits the entered values and shows the served report", async () => {
    compose.mockResolvedValue(served("Register lists X"));
    setup();
    fireEvent.change(screen.getByLabelText(/Claim id/), { target: { value: "c-1" } });
    fireEvent.change(screen.getByLabelText(/Claim text/), { target: { value: "Register lists X" } });
    fireEvent.change(screen.getByLabelText(/Citations, one per line/), { target: { value: "ev1:abc | p | 1" } });
    fireEvent.change(screen.getByLabelText(/Human verification/), { target: { value: "verified" } });
    submit();
    expect(compose).toHaveBeenCalledWith({
      claimId: "c-1", text: "Register lists X", currentState: true, historicalAsOf: "", citationLines: "ev1:abc | p | 1", humanVerification: "verified",
    });
    expect(await screen.findByRole("heading", { name: "Source-backed report" })).toBeTruthy();
    expect(screen.getByText("ev1:abc")).toBeTruthy();
  });

  it("disables the as-of date for a current-state claim and enables it otherwise", () => {
    setup();
    const asOf = screen.getByLabelText(/Historical as-of date/) as HTMLInputElement;
    expect(asOf.disabled).toBe(true);
    fireEvent.click(screen.getByLabelText(/Current-state claim/));
    expect(asOf.disabled).toBe(false);
  });

  it("shows blocker codes and no claim text for a blocked answer", async () => {
    compose.mockResolvedValue({ decision: "block", blockers: [{ code: "citation_not_verified", claimId: "c-1", detail: null }] });
    setup();
    fireEvent.change(screen.getByLabelText(/Claim text/), { target: { value: "Secret claim body" } });
    submit();
    expect(await screen.findByRole("heading", { name: "Report blocked" })).toBeTruthy();
    expect(screen.getByText(/citation_not_verified/)).toBeTruthy();
    expect(screen.queryByText(/Secret claim body/, { selector: "p, li" })).toBeNull();
  });

  it("ignores a slow earlier response that resolves after a newer submission", async () => {
    const first = deferred();
    const second = deferred();
    compose.mockReturnValueOnce(first.promise).mockReturnValueOnce(second.promise);
    setup();
    submit();
    submit();
    second.resolve(served("new text"));
    await waitFor(() => expect(screen.getByText("new text")).toBeTruthy());
    await act(async () => {
      first.resolve(served("old text"));
      await first.promise;
    });
    expect(screen.queryByText("old text")).toBeNull();
    expect(screen.getByText("new text")).toBeTruthy();
  });

  it("shows invalid-input and server errors without any report, and recovers", async () => {
    compose.mockImplementationOnce(() => Promise.reject(new InvalidComposeInput("Cite at least one evidence item.")));
    setup();
    submit();
    expect((await screen.findByRole("alert")).textContent).toContain("Cite at least one");
    compose.mockImplementationOnce(() => Promise.reject({ status: 503 }));
    submit();
    await waitFor(() => expect(screen.getByRole("alert").textContent).toContain("HTTP 503"));
    compose.mockResolvedValueOnce(served("Recovered"));
    submit();
    await waitFor(() => expect(screen.getByText("Recovered")).toBeTruthy());
    expect(screen.queryByRole("alert")).toBeNull();
  });

  it("links to sign-in carrying the current location on 401", async () => {
    compose.mockImplementationOnce(() => Promise.reject({ status: 401 }));
    setup();
    submit();
    await screen.findByRole("alert");
    expect((screen.getByRole("link", { name: "Sign in" }) as HTMLAnchorElement).getAttribute("href")).toBe("/login?returnTo=%2Fanswer-report%3Fx%3D1");
  });
});
