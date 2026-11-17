// @vitest-environment jsdom
import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MemoryRouter } from "react-router-dom";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { MalformedFetchAttemptsResponse, type FetchAttempt } from "../api/tenantFetchAttempts";
import type { TenantSource } from "../api/tenantSources";
import { SourcesPage } from "./SourcesPage";

// SYNTHETIC fixtures only. The network-facing clients are mocked; rendering and failure mapping are real.
const listSources = vi.hoisted(() => vi.fn());
const listAttempts = vi.hoisted(() => vi.fn());
vi.mock("../api/tenantSources", async (importOriginal) => ({
  ...(await importOriginal<typeof import("../api/tenantSources")>()),
  listTenantSources: listSources,
}));
vi.mock("../api/tenantFetchAttempts", async (importOriginal) => ({
  ...(await importOriginal<typeof import("../api/tenantFetchAttempts")>()),
  listFetchAttempts: listAttempts,
}));

const A: TenantSource = { id: "11111111-1111-4111-8111-111111111111", canonicalKey: "alpha.example", createdAtUtc: "2026-01-02T03:04:05Z" };
const B: TenantSource = { id: "22222222-2222-4222-8222-222222222222", canonicalKey: "beta.example", createdAtUtc: "2026-01-02T03:04:05Z" };
const attempt = (n: number, over: Partial<FetchAttempt> = {}): FetchAttempt => ({
  id: `${String(n).repeat(8)}-3333-4333-8333-333333333333`,
  requestedUri: `https://alpha.example/r${n}`,
  attemptedAtUtc: "2026-02-03T04:05:06Z",
  outcome: "succeeded",
  failureClass: null,
  httpStatus: 200,
  ...over,
});
const setup = () =>
  render(
    <QueryClientProvider client={new QueryClient()}>
      <MemoryRouter initialEntries={["/sources?x=1"]}>
        <SourcesPage />
      </MemoryRouter>
    </QueryClientProvider>,
  );
// The accessible description of each button is its source key; the name stays "View attempts".
const open = async (key: string) => {
  await screen.findByText(key);
  const button = (await screen.findAllByRole("button", { name: "View attempts" })).find((b) => b.getAttribute("aria-describedby") && document.getElementById(b.getAttribute("aria-describedby")!)?.textContent === key);
  expect(button).toBeTruthy();
  fireEvent.click(button!);
};

beforeEach(() => {
  listSources.mockReset();
  listAttempts.mockReset();
  listSources.mockResolvedValue([A, B]);
});
afterEach(cleanup);

describe("SourcesPage fetch attempts", () => {
  it("requests the selected source id and shows outcome, failure class and status", async () => {
    listAttempts.mockResolvedValue([attempt(1), attempt(2, { outcome: "failed", failureClass: "timeout", httpStatus: null })]);
    setup();
    await open("alpha.example");
    expect(await screen.findByText("https://alpha.example/r2")).toBeTruthy();
    expect(listAttempts).toHaveBeenCalledWith(A.id);
    expect(screen.getByText("(timeout)")).toBeTruthy();
    expect(screen.getByText("HTTP 200")).toBeTruthy();
    expect(screen.getAllByRole("note").some((n) => n.textContent?.includes("not proof that the retrieved record"))).toBe(true);
  });

  it("shows an explicit empty state", async () => {
    listAttempts.mockResolvedValue([]);
    setup();
    await open("alpha.example");
    expect(await screen.findByText(/No fetch attempts are recorded/)).toBeTruthy();
  });

  it("renders attempt text as text, never as markup", async () => {
    listAttempts.mockResolvedValue([attempt(1, { requestedUri: "<img src=x onerror=alert(1)>" })]);
    const { container } = setup();
    await screen.findByText("alpha.example");
    await open("alpha.example");
    await screen.findByText("<img src=x onerror=alert(1)>");
    expect(container.querySelector("img")).toBeNull();
  });

  it("shows the administrator message on 403 without echoing server text", async () => {
    listAttempts.mockRejectedValue({ status: 403, message: "SECRET" });
    setup();
    await open("alpha.example");
    const alert = await screen.findByRole("alert");
    expect(alert.textContent).toContain("administrator");
    expect(alert.textContent).not.toContain("SECRET");
    expect(screen.queryByRole("link", { name: "Sign in" })).toBeNull();
  });

  it("offers sign-in only on 401 and rejects a malformed response with the fixed message", async () => {
    listAttempts.mockRejectedValueOnce({ status: 401 });
    setup();
    await open("alpha.example");
    expect((await screen.findByRole("link", { name: "Sign in" })).getAttribute("href")).toBe(`/login?returnTo=${encodeURIComponent("/sources?x=1")}`);
    listAttempts.mockRejectedValueOnce(new MalformedFetchAttemptsResponse("item 0"));
    await open("beta.example");
    await waitFor(() => expect(screen.getByRole("alert").textContent).toContain("unexpected response"));
    expect(screen.queryByRole("link", { name: "Sign in" })).toBeNull();
  });

  it("drops a slow answer for a previously selected source", async () => {
    let resolveSlow: (rows: FetchAttempt[]) => void = () => {};
    listAttempts.mockImplementationOnce(() => new Promise((r) => { resolveSlow = r; }));
    listAttempts.mockResolvedValueOnce([attempt(2, { requestedUri: "https://beta.example/fast" })]);
    setup();
    await open("alpha.example");
    await open("beta.example");
    await screen.findByText("https://beta.example/fast");
    resolveSlow([attempt(1, { requestedUri: "https://alpha.example/stale" })]);
    await new Promise((r) => setTimeout(r, 10));
    expect(screen.queryByText("https://alpha.example/stale")).toBeNull();
    expect(screen.getByRole("heading", { name: /beta\.example/ })).toBeTruthy();
  });
});
