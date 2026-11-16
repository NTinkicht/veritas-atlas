// @vitest-environment jsdom
import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { Investigation, InvestigationShare } from "../api/investigations";
import { InvestigationSharesPanel } from "./InvestigationSharesPanel";

// SYNTHETIC fixtures only. The network-facing client calls are mocked; validation and rendering are real.
const api = vi.hoisted(() => ({ listShares: vi.fn(), grantShare: vi.fn(), revokeShare: vi.fn() }));
vi.mock("../api/investigations", async (importOriginal) => ({
  ...(await importOriginal<typeof import("../api/investigations")>()),
  ...api,
}));

const A: Investigation = { id: "11111111-1111-4111-8111-111111111111", title: "A", createdBySubject: "analyst-1", createdAtUtc: "2026-11-02T09:00:00Z", state: "open", version: 0 };
const B: Investigation = { ...A, id: "22222222-2222-4222-8222-222222222222", title: "B" };
const share = (inv: Investigation, who: string): InvestigationShare => ({ investigationId: inv.id, granteeSubject: who, grantedBySubject: "analyst-1", grantedAtUtc: "2026-11-02T09:00:00Z" });
const setup = (inv: Investigation = A) => render(<InvestigationSharesPanel investigation={inv} signInHref="/login" />);
const type = (v: string) => fireEvent.change(screen.getByLabelText("Share with (account name)"), { target: { value: v } });
const shareButton = () => screen.getByRole("button", { name: "Share" }) as HTMLButtonElement;

beforeEach(() => {
  Object.values(api).forEach((f) => f.mockReset());
  api.listShares.mockResolvedValue([]);
});
afterEach(cleanup);

describe("InvestigationSharesPanel", () => {
  it("lists active shares and shows an honest empty state", async () => {
    api.listShares.mockResolvedValue([share(A, "reviewer-1")]);
    setup();
    expect(await screen.findByText("reviewer-1")).toBeTruthy();
    cleanup();
    api.listShares.mockResolvedValue([]);
    setup();
    expect(await screen.findByText("This investigation is not shared with anyone.")).toBeTruthy();
  });

  it("keeps Share disabled and sends nothing for an invalid grantee, and enables it for a valid one", async () => {
    setup();
    await screen.findByText("This investigation is not shared with anyone.");
    type(" padded");
    expect(shareButton().disabled).toBe(true);
    fireEvent.submit(screen.getByRole("form", { name: "Share this investigation" }));
    expect(api.grantShare).not.toHaveBeenCalled();
    type("reviewer-1");
    expect(shareButton().disabled).toBe(false);
  });

  it("adds the grantee only after the server confirms, and a replay is not shown twice", async () => {
    api.listShares.mockResolvedValue([share(A, "reviewer-1")]);
    api.grantShare.mockResolvedValue(share(A, "reviewer-1"));
    setup();
    await screen.findByText("reviewer-1");
    type("reviewer-1");
    fireEvent.click(shareButton());
    await waitFor(() => expect(api.grantShare).toHaveBeenCalledWith(A.id, "reviewer-1"));
    await waitFor(() => expect((screen.getByLabelText("Share with (account name)") as HTMLInputElement).value).toBe(""));
    expect(screen.getAllByText("reviewer-1")).toHaveLength(1);
  });

  it("shows a refusal and does not add the grantee when the server answers 404", async () => {
    api.grantShare.mockRejectedValue({ status: 404 });
    setup();
    await screen.findByText("This investigation is not shared with anyone.");
    type("reviewer-2");
    fireEvent.click(shareButton());
    expect((await screen.findByRole("alert")).textContent).toContain("not found");
    expect(screen.queryByText("reviewer-2")).toBeNull();
  });

  it("removes a share only after the server confirms the revoke", async () => {
    api.listShares.mockResolvedValue([share(A, "reviewer-1")]);
    api.revokeShare.mockResolvedValue(undefined);
    setup();
    fireEvent.click(await screen.findByRole("button", { name: "Revoke access for reviewer-1" }));
    await waitFor(() => expect(api.revokeShare).toHaveBeenCalledWith(A.id, "reviewer-1"));
    await waitFor(() => expect(screen.queryByText("reviewer-1")).toBeNull());
  });

  it("keeps the share when the revoke is refused", async () => {
    api.listShares.mockResolvedValue([share(A, "reviewer-1")]);
    api.revokeShare.mockRejectedValue({ status: 500 });
    setup();
    fireEvent.click(await screen.findByRole("button", { name: "Revoke access for reviewer-1" }));
    expect((await screen.findByRole("alert")).textContent).toContain("HTTP 500");
    expect(screen.getByText("reviewer-1")).toBeTruthy();
  });

  it("disables every change for a closed investigation and sends nothing", async () => {
    api.listShares.mockResolvedValue([share(A, "reviewer-1")]);
    setup({ ...A, state: "closed" });
    const revoke = (await screen.findByRole("button", { name: "Revoke access for reviewer-1" })) as HTMLButtonElement;
    expect(revoke.disabled).toBe(true);
    type("reviewer-2");
    expect(shareButton().disabled).toBe(true);
    fireEvent.click(revoke);
    fireEvent.submit(screen.getByRole("form", { name: "Share this investigation" }));
    expect(api.revokeShare).not.toHaveBeenCalled();
    expect(api.grantShare).not.toHaveBeenCalled();
  });

  it("shows the read failure instead of an empty list", async () => {
    api.listShares.mockRejectedValue({ status: 401 });
    setup();
    expect((await screen.findByRole("alert")).textContent).toContain("Sign in");
    expect(screen.queryByText("This investigation is not shared with anyone.")).toBeNull();
  });

  it("reloads the shares after a failed read and enables sharing once it succeeds", async () => {
    api.listShares.mockRejectedValueOnce({ status: 503 });
    api.listShares.mockResolvedValueOnce([share(A, "reviewer-1")]);
    setup();
    expect((await screen.findByRole("alert")).textContent).toContain("HTTP 503");
    type("reviewer-2");
    expect(shareButton().disabled).toBe(true);
    fireEvent.click(screen.getByRole("button", { name: "Reload shares" }));
    expect(await screen.findByText("reviewer-1")).toBeTruthy();
    expect(screen.queryByRole("alert")).toBeNull();
    expect(shareButton().disabled).toBe(false);
    expect(api.listShares).toHaveBeenCalledTimes(2);
  });

  it("ignores a late read for the previously selected investigation", async () => {
    let resolveA: (v: InvestigationShare[]) => void = () => undefined;
    api.listShares.mockImplementationOnce(() => new Promise((r) => (resolveA = r)));
    api.listShares.mockResolvedValueOnce([share(B, "reviewer-b")]);
    const view = setup(A);
    view.rerender(<InvestigationSharesPanel investigation={B} signInHref="/login" />);
    expect(await screen.findByText("reviewer-b")).toBeTruthy();
    resolveA([share(A, "reviewer-a")]);
    await Promise.resolve();
    expect(screen.queryByText("reviewer-a")).toBeNull();
  });
});
