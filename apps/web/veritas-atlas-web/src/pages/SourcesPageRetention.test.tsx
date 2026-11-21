// @vitest-environment jsdom
import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MemoryRouter } from "react-router-dom";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { RawRetrieval } from "../api/tenantRawRetrievals";
import { MalformedRetentionResponse, type RetentionView } from "../api/tenantRetention";
import type { TenantSource } from "../api/tenantSources";
import { SourcesPage } from "./SourcesPage";

// SYNTHETIC fixtures only. The network-facing clients are mocked; rendering and failure mapping are real.
const listSources = vi.hoisted(() => vi.fn());
const listRetrievals = vi.hoisted(() => vi.fn());
const getRetention = vi.hoisted(() => vi.fn());
vi.mock("../api/tenantSources", async (importOriginal) => ({ ...(await importOriginal<typeof import("../api/tenantSources")>()), listTenantSources: listSources }));
vi.mock("../api/tenantRawRetrievals", async (importOriginal) => ({ ...(await importOriginal<typeof import("../api/tenantRawRetrievals")>()), listRawRetrievals: listRetrievals }));
vi.mock("../api/tenantRetention", async (importOriginal) => ({ ...(await importOriginal<typeof import("../api/tenantRetention")>()), getRetentionView: getRetention }));

const A: TenantSource = { id: "11111111-1111-4111-8111-111111111111", canonicalKey: "alpha.example", createdAtUtc: "2026-01-02T03:04:05Z" };
const retrieval = (n: number): RawRetrieval => ({
  id: `${String(n).repeat(8)}-3333-4333-8333-333333333333`,
  finalUri: `https://alpha.example/r${n}`,
  contentSha256: String(n).repeat(64),
  byteLength: 100 + n,
  retrievedAtUtc: "2026-02-03T04:05:06Z",
});
const denied = (over: Partial<RetentionView["eligibility"]> = {}): RetentionView => ({
  eligibility: { outcome: "Deny", denialCode: "legal_hold", parseRunCount: 2, attestationCount: 1, recordVersion: 2, legalHold: true, retainUntilUtc: "2027-01-02T03:04:05Z", ...over },
  history: [
    { version: 1, legalHold: false, retainUntilUtc: "2027-01-02T03:04:05Z", actorSubject: "admin-one", createdAtUtc: "2026-03-01T00:00:00Z" },
    { version: 2, legalHold: true, retainUntilUtc: "2027-01-02T03:04:05Z", actorSubject: "admin-two", createdAtUtc: "2026-03-02T00:00:00Z" },
  ],
});
const setup = () =>
  render(
    <QueryClientProvider client={new QueryClient()}>
      <MemoryRouter initialEntries={["/sources?x=1"]}>
        <SourcesPage />
      </MemoryRouter>
    </QueryClientProvider>,
  );
const openRetrievals = async () => {
  await screen.findByText("alpha.example");
  fireEvent.click(await screen.findByRole("button", { name: "View retrievals" }));
  await screen.findByText("https://alpha.example/r1");
};
const viewRetention = (uri: string) => {
  const b = screen.getAllByRole("button", { name: "View retention" }).find((x) => document.getElementById(x.getAttribute("aria-describedby")!)?.textContent === uri);
  expect(b).toBeTruthy();
  fireEvent.click(b!);
};

beforeEach(() => {
  listSources.mockReset().mockResolvedValue([A]);
  listRetrievals.mockReset().mockResolvedValue([retrieval(1), retrieval(2)]);
  getRetention.mockReset();
});
afterEach(cleanup);

describe("SourcesPage retention view", () => {
  it("requests the selected retrieval and shows the dry-run verdict, counts and history", async () => {
    getRetention.mockResolvedValue(denied());
    setup();
    await openRetrievals();
    viewRetention("https://alpha.example/r2");
    expect(await screen.findByText(/not eligible for purge/)).toBeTruthy();
    expect(getRetention).toHaveBeenCalledWith(retrieval(2).id);
    expect(screen.getByText(/A legal hold is recorded\./)).toBeTruthy();
    expect(screen.getByText(/2 parse run\(s\), 1 attestation\(s\), legal hold on/)).toBeTruthy();
    expect(screen.getByRole("list", { name: "Retention versions" }).querySelectorAll("li")).toHaveLength(2);
    expect(screen.getByText("admin-two")).toBeTruthy();
    expect(screen.getAllByRole("note").some((n) => n.textContent?.includes("never deletes anything"))).toBe(true);
    expect(screen.queryByRole("button", { name: /delete|purge|hold|release/i })).toBeNull();
  });

  it("states eligibility only as a dry run and shows an empty history", async () => {
    getRetention.mockResolvedValue({ eligibility: { ...denied().eligibility, outcome: "Allow", denialCode: null, legalHold: false }, history: [] });
    setup();
    await openRetrievals();
    viewRetention("https://alpha.example/r1");
    expect(await screen.findByText(/would be eligible for purge/)).toBeTruthy();
    expect(screen.getByText(/No retention version is recorded/)).toBeTruthy();
    expect(screen.queryByText(/ev1:/)).toBeNull();
  });

  it("renders actor text as text, never as markup", async () => {
    const view = denied();
    view.history[0].actorSubject = "<img src=x onerror=alert(1)>";
    getRetention.mockResolvedValue(view);
    const { container } = setup();
    await openRetrievals();
    viewRetention("https://alpha.example/r1");
    await screen.findByText("<img src=x onerror=alert(1)>");
    expect(container.querySelector("img")).toBeNull();
  });

  it("maps 403 and 401 to fixed messages without echoing server text", async () => {
    getRetention.mockRejectedValueOnce({ status: 403, message: "SECRET" });
    setup();
    await openRetrievals();
    viewRetention("https://alpha.example/r1");
    const alert = await screen.findByRole("alert");
    expect(alert.textContent).toContain("administrator");
    expect(alert.textContent).not.toContain("SECRET");
    expect(screen.queryByRole("link", { name: "Sign in" })).toBeNull();
    getRetention.mockRejectedValueOnce({ status: 401 });
    viewRetention("https://alpha.example/r1");
    expect((await screen.findByRole("link", { name: "Sign in" })).getAttribute("href")).toBe(`/login?returnTo=${encodeURIComponent("/sources?x=1")}`);
  });

  it("rejects a malformed response with the fixed message", async () => {
    getRetention.mockRejectedValueOnce(new MalformedRetentionResponse("outcome"));
    setup();
    await openRetrievals();
    viewRetention("https://alpha.example/r1");
    await waitFor(() => expect(screen.getByRole("alert").textContent).toContain("unexpected response"));
  });

  it("drops a slow answer for a previously selected retrieval", async () => {
    let resolveSlow: (v: RetentionView) => void = () => {};
    getRetention.mockImplementationOnce(() => new Promise((r) => { resolveSlow = r; }));
    getRetention.mockResolvedValueOnce(denied({ denialCode: "live_reference" }));
    setup();
    await openRetrievals();
    viewRetention("https://alpha.example/r1");
    viewRetention("https://alpha.example/r2");
    await screen.findByText(/A parse run still references these bytes/);
    resolveSlow(denied({ denialCode: "audit_trail_missing" }));
    await new Promise((r) => setTimeout(r, 10));
    expect(screen.queryByText(/no audit trail/)).toBeNull();
    expect(screen.getByRole("heading", { name: /^Retention of/ }).textContent).toContain(retrieval(2).id);
  });
});
