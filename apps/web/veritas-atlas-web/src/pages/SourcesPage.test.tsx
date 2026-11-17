// @vitest-environment jsdom
import { cleanup, render, screen } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MemoryRouter } from "react-router-dom";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { MalformedTenantSourcesResponse, type TenantSource } from "../api/tenantSources";
import { SourcesPage } from "./SourcesPage";

// SYNTHETIC fixtures only. The network-facing client is mocked; rendering and failure mapping are real.
const list = vi.hoisted(() => vi.fn());
vi.mock("../api/tenantSources", async (importOriginal) => ({
  ...(await importOriginal<typeof import("../api/tenantSources")>()),
  listTenantSources: list,
}));

const src = (n: number, canonicalKey: string): TenantSource => ({
  id: `${String(n).repeat(8)}-1111-4111-8111-111111111111`, canonicalKey, createdAtUtc: "2026-01-02T03:04:05Z",
});
const setup = () =>
  render(
    <QueryClientProvider client={new QueryClient()}>
      <MemoryRouter initialEntries={["/sources?x=1"]}>
        <SourcesPage />
      </MemoryRouter>
    </QueryClientProvider>,
  );

// Rejects on a later tick, after the page has attached its handlers (a promise rejected before any handler
// exists is reported as unhandled by the test runner).
const failWith = (error: unknown) => list.mockImplementationOnce(() => new Promise((_, reject) => setTimeout(() => reject(error), 0)));

beforeEach(() => list.mockReset());
afterEach(cleanup);

describe("SourcesPage", () => {
  it("shows loading, then the registered sources with the registry-only note", async () => {
    list.mockResolvedValue([src(1, "gazette.example/register"), src(2, "other.example")]);
    setup();
    expect(screen.getByRole("status").textContent).toContain("Loading");
    expect(await screen.findByText("gazette.example/register")).toBeTruthy();
    expect(list).toHaveBeenCalledTimes(1);
    expect(screen.getByText("other.example")).toBeTruthy();
    expect(screen.getByRole("heading", { name: /Registered sources \(2\)/ })).toBeTruthy();
    expect(screen.getByRole("note").textContent).toContain("not proof");
    expect(screen.queryByRole("button", { name: /register|add|create|edit|fetch/i })).toBeNull();
  });

  it("shows an explicit empty state", async () => {
    list.mockResolvedValue([]);
    setup();
    expect(await screen.findByText(/No sources are registered/)).toBeTruthy();
    expect(screen.queryByRole("heading", { name: /Registered sources/ })).toBeNull();
  });

  it("renders source text as text, never as markup", async () => {
    list.mockResolvedValue([src(1, "<img src=x onerror=alert(1)>")]);
    const { container } = setup();
    await screen.findByText("<img src=x onerror=alert(1)>");
    expect(container.querySelector("img")).toBeNull();
  });

  it("offers sign-in only on 401, preserving returnTo", async () => {
    failWith({ status: 401 });
    setup();
    const link = await screen.findByRole("link", { name: "Sign in" });
    expect(list).toHaveBeenCalledTimes(1);
    expect(link.getAttribute("href")).toBe(`/login?returnTo=${encodeURIComponent("/sources?x=1")}`);
  });

  it("shows the organization message on 403 without a sign-in link", async () => {
    failWith({ status: 403, message: "SECRET" });
    setup();
    expect((await screen.findByRole("alert")).textContent).toContain("needs an organization");
    expect(screen.queryByRole("alert")?.textContent).not.toContain("SECRET");
    expect(screen.queryByRole("link", { name: "Sign in" })).toBeNull();
  });

  it("shows nothing but the fixed malformed message for a rejected response", async () => {
    failWith(new MalformedTenantSourcesResponse("items"));
    setup();
    expect((await screen.findByRole("alert")).textContent).toContain("unexpected response");
    expect(screen.queryByRole("heading", { name: /Registered sources/ })).toBeNull();
  });
});
