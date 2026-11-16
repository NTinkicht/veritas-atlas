// @vitest-environment jsdom
import { cleanup, fireEvent, render, screen } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MemoryRouter } from "react-router-dom";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { InvalidCandidatesInput, type ClaimCandidatesResult } from "../api/claimCandidates";
import { ClaimCandidatesPage } from "./ClaimCandidatesPage";

// SYNTHETIC fixtures only. The network-facing client is mocked; rendering and failure mapping are real.
const list = vi.hoisted(() => vi.fn());
vi.mock("../api/claimCandidates", async (importOriginal) => ({
  ...(await importOriginal<typeof import("../api/claimCandidates")>()),
  listClaimCandidates: list,
}));

const result = (claims: ClaimCandidatesResult["claims"], eligible = true): ClaimCandidatesResult => ({
  evidenceRef: "ev1:abc", extractorName: "claim-extractor", extractorVersion: "1",
  freshness: { state: eligible ? "current" : "stale", eligible, reason: null, evaluatedAt: null }, claims,
});
const cand = (key: string, quotation: string) => ({
  claimKey: key, quotation, elementId: "e1", start: 3, length: quotation.length, chunkTextSha256: "ab".repeat(32), status: "unconfirmed" as const,
});
type Deferred = { promise: Promise<ClaimCandidatesResult>; resolve: (v: ClaimCandidatesResult) => void };
function deferred(): Deferred {
  let resolve!: Deferred["resolve"];
  const promise = new Promise<ClaimCandidatesResult>((res) => {
    resolve = res;
  });
  return { promise, resolve };
}

const setup = () =>
  render(
    <QueryClientProvider client={new QueryClient()}>
      <MemoryRouter initialEntries={["/claim-candidates?x=1"]}>
        <ClaimCandidatesPage />
      </MemoryRouter>
    </QueryClientProvider>,
  );
const fill = () => fireEvent.change(screen.getByLabelText(/Evidence reference/), { target: { value: "ev1:abc" } });
const submit = () => fireEvent.click(screen.getByRole("button", { name: "List claim candidates" }));

beforeEach(() => list.mockReset());
afterEach(cleanup);

describe("ClaimCandidatesPage", () => {
  it("makes no request before submission", () => {
    setup();
    expect(screen.queryByRole("heading", { name: "Claim candidates", level: 2 })).toBeNull();
    expect(list).not.toHaveBeenCalled();
  });

  it("lists quotations as unconfirmed and not answerable, with the not-a-claim note", async () => {
    list.mockResolvedValue(result([cand("k1", "The register lists X."), cand("k2", "Y was repealed.")]));
    setup();
    fill();
    submit();
    expect(list).toHaveBeenCalledWith("ev1:abc");
    expect(await screen.findByText("The register lists X.")).toBeTruthy();
    expect(screen.getByText("Y was repealed.")).toBeTruthy();
    expect(screen.getAllByText(/Unconfirmed, not answerable/)).toHaveLength(2);
    expect(screen.getByRole("note").textContent).toContain("not claims");
  });

  it("renders quotation text as text, never as markup", async () => {
    list.mockResolvedValue(result([cand("k1", "<img src=x onerror=alert(1)>")]));
    const { container } = setup();
    fill();
    submit();
    await screen.findByText("<img src=x onerror=alert(1)>");
    expect(container.querySelector("img")).toBeNull();
  });

  it("flags stale evidence as not eligible as current-state evidence", async () => {
    list.mockResolvedValue(result([cand("k1", "Q.")], false));
    setup();
    fill();
    submit();
    expect(await screen.findByText(/not eligible as current-state evidence/)).toBeTruthy();
  });

  it("shows an explicit empty state", async () => {
    list.mockResolvedValue(result([]));
    setup();
    fill();
    submit();
    expect(await screen.findByText(/No claim candidates were found/)).toBeTruthy();
  });

  it("shows invalid input as an alert", async () => {
    list.mockImplementationOnce(() => Promise.reject(new InvalidCandidatesInput("Enter an evidence reference of 1-512 characters.")));
    setup();
    submit();
    expect((await screen.findByRole("alert")).textContent).toContain("Enter an evidence reference");
  });

  it("links to sign-in with a return path on 401 only", async () => {
    list.mockImplementationOnce(() => Promise.reject({ status: 401 }));
    setup();
    fill();
    submit();
    const link = await screen.findByRole("link", { name: "Sign in" });
    expect(link.getAttribute("href")).toBe(`/login?returnTo=${encodeURIComponent("/claim-candidates?x=1")}`);
    list.mockImplementationOnce(() => Promise.reject({ status: 404 }));
    submit();
    expect(await screen.findByText(/No verified evidence with that reference/)).toBeTruthy();
    expect(screen.queryByRole("link", { name: "Sign in" })).toBeNull();
  });

  it("maps a 409 to the integrity message and shows no candidates", async () => {
    list.mockImplementationOnce(() => Promise.reject({ status: 409 }));
    setup();
    fill();
    submit();
    expect(await screen.findByText(/failed integrity checks/)).toBeTruthy();
    expect(screen.queryByText(/Unconfirmed, not answerable/)).toBeNull();
  });

  it("shows only the latest request's result", async () => {
    const first = deferred();
    const second = deferred();
    list.mockReturnValueOnce(first.promise).mockReturnValueOnce(second.promise);
    setup();
    fill();
    submit();
    submit();
    second.resolve(result([cand("k2", "Second.")]));
    expect(await screen.findByText("Second.")).toBeTruthy();
    first.resolve(result([cand("k1", "First.")]));
    await Promise.resolve();
    expect(screen.queryByText("First.")).toBeNull();
    expect(screen.getByText("Second.")).toBeTruthy();
  });
});
