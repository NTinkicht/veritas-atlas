// @vitest-environment jsdom
import { act, cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MemoryRouter } from "react-router-dom";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { InvalidConfidenceInput, type ClaimConfidenceLabel } from "../api/claimConfidence";
import { ClaimConfidencePage } from "./ClaimConfidencePage";

// SYNTHETIC fixtures only. The network-facing labeler is mocked; classification and rendering are real.
const label = vi.hoisted(() => vi.fn());
vi.mock("../api/claimConfidence", async (importOriginal) => ({
  ...(await importOriginal<typeof import("../api/claimConfidence")>()),
  labelClaimConfidence: label,
}));

type Deferred = { promise: Promise<ClaimConfidenceLabel>; resolve: (v: ClaimConfidenceLabel) => void; reject: (e: unknown) => void };
function deferred(): Deferred {
  let resolve!: Deferred["resolve"];
  let reject!: Deferred["reject"];
  const promise = new Promise<ClaimConfidenceLabel>((res, rej) => {
    resolve = res;
    reject = rej;
  });
  return { promise, resolve, reject };
}

function setup() {
  render(
    <QueryClientProvider client={new QueryClient()}>
      <MemoryRouter initialEntries={["/claim-confidence?x=1"]}>
        <ClaimConfidencePage />
      </MemoryRouter>
    </QueryClientProvider>,
  );
}
const submit = () => fireEvent.click(screen.getByRole("button", { name: "Label confidence" }));
const typeId = (v: string) => fireEvent.change(screen.getByLabelText(/Claim id/), { target: { value: v } });

beforeEach(() => label.mockReset());
afterEach(cleanup);

describe("ClaimConfidencePage", () => {
  it("renders the form with no result before any submission", () => {
    setup();
    expect(screen.getByLabelText(/Claim id/)).toBeTruthy();
    expect(screen.getByLabelText(/Supporting evidence/)).toBeTruthy();
    expect(screen.queryByText(/Evidence confidence:/)).toBeNull();
    expect(label).not.toHaveBeenCalled();
  });

  it("submits the claim id and entered counts, then shows loading and the server band", async () => {
    const d = deferred();
    label.mockReturnValue(d.promise);
    setup();
    typeId("c-1");
    fireEvent.change(screen.getByLabelText(/Supporting evidence/), { target: { value: "3" } });
    submit();
    expect(label).toHaveBeenCalledWith("c-1", { supportingEvidenceCount: "3" });
    expect(screen.queryByText(/Evidence confidence:/)).toBeNull();
    d.resolve({ claimId: "c-1", band: "high", reasons: ["multiple_independent_sources"] });
    await waitFor(() => expect(screen.getByText(/Evidence confidence: high/)).toBeTruthy());
    expect(screen.getByText(/Several independent sources support the claim\./)).toBeTruthy();
  });

  it("ignores a slow earlier response that resolves after a newer submission (ref race protection)", async () => {
    const first = deferred();
    const second = deferred();
    label.mockReturnValueOnce(first.promise).mockReturnValueOnce(second.promise);
    setup();
    typeId("old");
    submit();
    typeId("new");
    submit();
    second.resolve({ claimId: "new", band: "medium", reasons: [] });
    await waitFor(() => expect(screen.getByText(/Evidence confidence: medium/)).toBeTruthy());
    await act(async () => {
      first.resolve({ claimId: "old", band: "high", reasons: [] });
      await first.promise;
    });
    expect(screen.queryByText(/Evidence confidence: high/)).toBeNull();
    expect(screen.getByText(/Evidence confidence: medium/)).toBeTruthy();
  });

  it("ignores a slow earlier failure that settles after a newer success", async () => {
    const first = deferred();
    const second = deferred();
    label.mockReturnValueOnce(first.promise).mockReturnValueOnce(second.promise);
    setup();
    typeId("old");
    submit();
    typeId("new");
    submit();
    second.resolve({ claimId: "new", band: "low", reasons: [] });
    await waitFor(() => expect(screen.getByText(/Evidence confidence: low/)).toBeTruthy());
    await act(async () => {
      first.reject({ status: 500 });
      await first.promise.catch(() => undefined);
    });
    expect(screen.queryByRole("alert")).toBeNull();
    expect(screen.getByText(/Evidence confidence: low/)).toBeTruthy();
  });

  it("shows invalid-input errors without any band", async () => {
    label.mockImplementationOnce(() => Promise.reject(new InvalidConfidenceInput("Enter a claim id of 1-500 characters.")));
    setup();
    submit();
    const alert = await screen.findByRole("alert");
    expect(alert.textContent).toContain("Enter a claim id");
    expect(screen.queryByText(/Evidence confidence:/)).toBeNull();
  });

  it("shows a sign-in link carrying the current location on 401, never a band", async () => {
    label.mockImplementationOnce(() => Promise.reject({ status: 401 }));
    setup();
    typeId("c-1");
    submit();
    const alert = await screen.findByRole("alert");
    expect(alert.textContent).toContain("Sign in again");
    const link = screen.getByRole("link", { name: "Sign in" }) as HTMLAnchorElement;
    expect(link.getAttribute("href")).toBe("/login?returnTo=%2Fclaim-confidence%3Fx%3D1");
    expect(screen.queryByText(/Evidence confidence:/)).toBeNull();
  });

  it("shows a server failure instead of a guessed band, and recovers on a later success", async () => {
    label.mockImplementationOnce(() => Promise.reject({ status: 503 }));
    setup();
    typeId("c-1");
    submit();
    expect((await screen.findByRole("alert")).textContent).toContain("HTTP 503");
    label.mockResolvedValueOnce({ claimId: "c-1", band: "insufficient", reasons: ["input_missing"] });
    submit();
    await waitFor(() => expect(screen.getByText(/Evidence confidence: insufficient/)).toBeTruthy());
    expect(screen.queryByRole("alert")).toBeNull();
  });
});
