// @vitest-environment jsdom
import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MemoryRouter } from "react-router-dom";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { MalformedRawRetrievalsResponse, type RawRetrieval } from "../api/tenantRawRetrievals";
import type { TenantSource } from "../api/tenantSources";
import { SourcesPage } from "./SourcesPage";

// SYNTHETIC fixtures only. The network-facing clients are mocked; rendering and failure mapping are real.
const listSources = vi.hoisted(() => vi.fn());
const listRetrievals = vi.hoisted(() => vi.fn());
vi.mock("../api/tenantSources", async (importOriginal) => ({
  ...(await importOriginal<typeof import("../api/tenantSources")>()),
  listTenantSources: listSources,
}));
vi.mock("../api/tenantRawRetrievals", async (importOriginal) => ({
  ...(await importOriginal<typeof import("../api/tenantRawRetrievals")>()),
  listRawRetrievals: listRetrievals,
}));

const A: TenantSource = { id: "11111111-1111-4111-8111-111111111111", canonicalKey: "alpha.example", createdAtUtc: "2026-01-02T03:04:05Z" };
const B: TenantSource = { id: "22222222-2222-4222-8222-222222222222", canonicalKey: "beta.example", createdAtUtc: "2026-01-02T03:04:05Z" };
const retrieval = (n: number, over: Partial<RawRetrieval> = {}): RawRetrieval => ({
  id: `${String(n).repeat(8)}-3333-4333-8333-333333333333`,
  finalUri: `https://alpha.example/r${n}`,
  contentSha256: String(n).repeat(64),
  byteLength: 100 + n,
  retrievedAtUtc: "2026-02-03T04:05:06Z",
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
const open = async (key: string) => {
  await screen.findByText(key);
  const button = (await screen.findAllByRole("button", { name: "View retrievals" })).find((b) => document.getElementById(b.getAttribute("aria-describedby")!)?.textContent === key);
  expect(button).toBeTruthy();
  fireEvent.click(button!);
};

beforeEach(() => {
  listRetrievals.mockReset();
  listSources.mockReset();
  listSources.mockResolvedValue([A, B]);
});
afterEach(cleanup);

describe("SourcesPage raw retrievals", () => {
  it("requests the selected source id and shows uri, digest and length", async () => {
    listRetrievals.mockResolvedValue([retrieval(1), retrieval(2)]);
    setup();
    await open("alpha.example");
    expect(await screen.findByText("https://alpha.example/r2")).toBeTruthy();
    expect(listRetrievals).toHaveBeenCalledWith(A.id);
    expect(screen.getByText("2".repeat(64))).toBeTruthy();
    expect(screen.getByText("101 bytes")).toBeTruthy();
    expect(screen.getAllByRole("note").some((n) => n.textContent?.includes("not verified evidence"))).toBe(true);
    expect(screen.queryByText(/ev1:/)).toBeNull();
  });

  it("shows an explicit empty state", async () => {
    listRetrievals.mockResolvedValue([]);
    setup();
    await open("alpha.example");
    expect(await screen.findByText(/No raw retrievals are recorded/)).toBeTruthy();
  });

  it("renders text as text, never as markup", async () => {
    listRetrievals.mockResolvedValue([retrieval(1, { finalUri: "<img src=x onerror=alert(1)>" })]);
    const { container } = setup();
    await open("alpha.example");
    await screen.findByText("<img src=x onerror=alert(1)>");
    expect(container.querySelector("img")).toBeNull();
  });

  it("shows the administrator message on 403 without echoing server text", async () => {
    listRetrievals.mockRejectedValue({ status: 403, message: "SECRET" });
    setup();
    await open("alpha.example");
    const alert = await screen.findByRole("alert");
    expect(alert.textContent).toContain("administrator");
    expect(alert.textContent).not.toContain("SECRET");
    expect(screen.queryByRole("link", { name: "Sign in" })).toBeNull();
  });

  it("offers sign-in only on 401 and rejects a malformed response with the fixed message", async () => {
    listRetrievals.mockRejectedValueOnce({ status: 401 });
    setup();
    await open("alpha.example");
    expect((await screen.findByRole("link", { name: "Sign in" })).getAttribute("href")).toBe(`/login?returnTo=${encodeURIComponent("/sources?x=1")}`);
    listRetrievals.mockRejectedValueOnce(new MalformedRawRetrievalsResponse("item 0"));
    await open("beta.example");
    await waitFor(() => expect(screen.getByRole("alert").textContent).toContain("unexpected response"));
    expect(screen.queryByRole("link", { name: "Sign in" })).toBeNull();
  });

  it("drops a slow answer for a previously selected source", async () => {
    let resolveSlow: (rows: RawRetrieval[]) => void = () => {};
    listRetrievals.mockImplementationOnce(() => new Promise((r) => { resolveSlow = r; }));
    listRetrievals.mockResolvedValueOnce([retrieval(2, { finalUri: "https://beta.example/fast" })]);
    setup();
    await open("alpha.example");
    await open("beta.example");
    await screen.findByText("https://beta.example/fast");
    resolveSlow([retrieval(1, { finalUri: "https://alpha.example/stale" })]);
    await new Promise((r) => setTimeout(r, 10));
    expect(screen.queryByText("https://alpha.example/stale")).toBeNull();
    expect(screen.getByRole("heading", { name: /beta\.example/ })).toBeTruthy();
  });
});
