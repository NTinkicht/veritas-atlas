// @vitest-environment jsdom
import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MemoryRouter } from "react-router-dom";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { Investigation } from "../api/investigations";
import { InvestigationBoardPage } from "./InvestigationBoardPage";

// SYNTHETIC fixtures only. The network-facing client calls are mocked; validation, merge and rendering are real.
const api = vi.hoisted(() => ({
  listInvestigations: vi.fn(),
  createInvestigation: vi.fn(),
  listEvidenceItems: vi.fn(),
  listBookmarks: vi.fn(),
}));
vi.mock("../api/investigations", async (importOriginal) => ({
  ...(await importOriginal<typeof import("../api/investigations")>()),
  ...api,
}));

const row = (id: string, title: string): Investigation => ({ id, title, createdBySubject: "analyst-1", createdAtUtc: "2026-11-02T09:00:00Z" });
const A = row("11111111-1111-4111-8111-111111111111", "Existing");
const B = row("22222222-2222-4222-8222-222222222222", "Fresh inquiry");

function setup() {
  render(
    <QueryClientProvider client={new QueryClient()}>
      <MemoryRouter initialEntries={["/investigations"]}>
        <InvestigationBoardPage />
      </MemoryRouter>
    </QueryClientProvider>,
  );
}
const type = (v: string) => fireEvent.change(screen.getByLabelText("New investigation title"), { target: { value: v } });
const submit = () => fireEvent.click(screen.getByRole("button", { name: /Open investigation|Opening/ }));
const keyOf = (call: number) => api.createInvestigation.mock.calls[call][1] as string;

beforeEach(() => {
  Object.values(api).forEach((f) => f.mockReset());
  api.listInvestigations.mockResolvedValue([A]);
  api.listEvidenceItems.mockResolvedValue([]);
  api.listBookmarks.mockResolvedValue([]);
});
afterEach(cleanup);

describe("InvestigationBoardPage create flow", () => {
  it("keeps submit disabled and sends nothing for an invalid title", async () => {
    setup();
    await screen.findByText("Existing");
    type(" padded");
    expect((screen.getByRole("button", { name: "Open investigation" }) as HTMLButtonElement).disabled).toBe(true);
    submit();
    expect(api.createInvestigation).not.toHaveBeenCalled();
  });

  it("creates, lists first, opens the new investigation and clears the title", async () => {
    api.createInvestigation.mockResolvedValue(B);
    setup();
    await screen.findByText("Existing");
    type("Fresh inquiry");
    submit();
    await waitFor(() => expect(api.listEvidenceItems).toHaveBeenCalledWith(B.id));
    expect(api.createInvestigation).toHaveBeenCalledWith("Fresh inquiry", expect.any(String));
    const items = screen.getAllByRole("listitem");
    expect(items[0].textContent).toContain("Fresh inquiry");
    expect((screen.getByLabelText("New investigation title") as HTMLInputElement).value).toBe("");
  });

  it("reuses the creation key on retry of the same title and rotates it for an edited title or after success", async () => {
    api.createInvestigation.mockRejectedValueOnce({ status: 500 }).mockRejectedValueOnce({ status: 500 }).mockResolvedValue(B);
    setup();
    await screen.findByText("Existing");
    type("Fresh inquiry");
    submit();
    await screen.findByRole("alert");
    submit();
    await waitFor(() => expect(api.createInvestigation).toHaveBeenCalledTimes(2));
    expect(keyOf(1)).toBe(keyOf(0));
    await screen.findByRole("alert");
    type("Fresh inquiry v2");
    submit();
    await waitFor(() => expect(api.createInvestigation).toHaveBeenCalledTimes(3));
    expect(keyOf(2)).not.toBe(keyOf(0));
    await waitFor(() => expect(api.listEvidenceItems).toHaveBeenCalled());
    type("Another");
    api.createInvestigation.mockResolvedValue(row("33333333-3333-4333-8333-333333333333", "Another"));
    submit();
    await waitFor(() => expect(api.createInvestigation).toHaveBeenCalledTimes(4));
    expect(keyOf(3)).not.toBe(keyOf(2));
  });

  it("shows a refusal as an alert, adds nothing and keeps the typed title", async () => {
    api.createInvestigation.mockRejectedValue({ status: 403 });
    setup();
    await screen.findByText("Existing");
    type("Fresh inquiry");
    submit();
    expect((await screen.findByRole("alert")).textContent).toContain("not permitted");
    expect(screen.queryByText("Fresh inquiry", { selector: "button" })).toBeNull();
    expect((screen.getByLabelText("New investigation title") as HTMLInputElement).value).toBe("Fresh inquiry");
    expect(api.listEvidenceItems).not.toHaveBeenCalled();
  });

  it("ignores a second submit while one is pending", async () => {
    let resolve!: (i: Investigation) => void;
    api.createInvestigation.mockReturnValue(new Promise<Investigation>((r) => (resolve = r)));
    setup();
    await screen.findByText("Existing");
    type("Fresh inquiry");
    submit();
    submit();
    expect(api.createInvestigation).toHaveBeenCalledTimes(1);
    resolve(B);
    await waitFor(() => expect(api.listEvidenceItems).toHaveBeenCalledWith(B.id));
  });

  it("keeps a creation that finishes before the initial list read, without duplicating it", async () => {
    let listed!: (v: Investigation[]) => void;
    api.listInvestigations.mockReturnValue(new Promise<Investigation[]>((r) => (listed = r)));
    api.createInvestigation.mockResolvedValue(B);
    setup();
    type("Fresh inquiry");
    submit();
    await waitFor(() => expect(api.listEvidenceItems).toHaveBeenCalledWith(B.id));
    listed([A, B]);
    await screen.findByText("Existing");
    const buttons = screen.getAllByRole("button").filter((b) => b.textContent === "Fresh inquiry");
    expect(buttons).toHaveLength(1);
    expect(screen.getAllByRole("listitem")[0].textContent).toContain("Fresh inquiry");
  });
});
