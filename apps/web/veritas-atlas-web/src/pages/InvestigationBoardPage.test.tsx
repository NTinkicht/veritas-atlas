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
  transitionInvestigation: vi.fn(),
  listEvidenceItems: vi.fn(),
  listBookmarks: vi.fn(),
  listShares: vi.fn(),
  collectEvidence: vi.fn(),
}));
vi.mock("../api/investigations", async (importOriginal) => ({
  ...(await importOriginal<typeof import("../api/investigations")>()),
  ...api,
}));

const row = (id: string, title: string): Investigation => ({ id, title, createdBySubject: "analyst-1", createdAtUtc: "2026-11-02T09:00:00Z", state: "open", version: 0 });
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
  api.listShares.mockResolvedValue([]);
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

describe("InvestigationBoardPage close and reopen", () => {
  const select = async () => {
    setup();
    fireEvent.click(await screen.findByRole("button", { name: "Existing" }));
    await screen.findByText("State:", { exact: false });
  };
  it("closes from the server answer, then reopens", async () => {
    api.transitionInvestigation
      .mockResolvedValueOnce({ ...A, state: "closed", version: 1 })
      .mockResolvedValueOnce({ ...A, state: "open", version: 2 });
    await select();
    expect(screen.getByText("Open", { selector: "strong" })).toBeTruthy();
    fireEvent.click(screen.getByRole("button", { name: "Close investigation" }));
    await screen.findByRole("button", { name: "Reopen investigation" });
    expect(api.transitionInvestigation).toHaveBeenNthCalledWith(1, A, true);
    expect(screen.getByText("Closed", { selector: "strong" })).toBeTruthy();
    expect(screen.getAllByRole("listitem")[0].textContent).toContain("closed;");
    fireEvent.click(screen.getByRole("button", { name: "Reopen investigation" }));
    await screen.findByRole("button", { name: "Close investigation" });
    expect(api.transitionInvestigation).toHaveBeenNthCalledWith(2, { ...A, state: "closed", version: 1 }, false);
  });
  it("keeps the old state and shows a notice on a 409 (not optimistic)", async () => {
    api.transitionInvestigation.mockRejectedValue({ status: 409 });
    await select();
    fireEvent.click(screen.getByRole("button", { name: "Close investigation" }));
    expect((await screen.findByRole("alert")).textContent).toContain("changed or is already");
    expect(screen.getByText("Open", { selector: "strong" })).toBeTruthy();
    expect(screen.queryByRole("button", { name: "Reopen investigation" })).toBeNull();
  });
  it("sends one request for a double click", async () => {
    let resolve!: (i: Investigation) => void;
    api.transitionInvestigation.mockReturnValue(new Promise<Investigation>((r) => (resolve = r)));
    await select();
    const button = screen.getByRole("button", { name: "Close investigation" });
    fireEvent.click(button);
    fireEvent.click(button);
    expect(api.transitionInvestigation).toHaveBeenCalledTimes(1);
    resolve({ ...A, state: "closed", version: 1 });
    await screen.findByRole("button", { name: "Reopen investigation" });
  });
  it("reconciles the open board when the same investigation is re-selected mid-flight", async () => {
    let resolve!: (i: Investigation) => void;
    api.transitionInvestigation.mockReturnValue(new Promise<Investigation>((r) => (resolve = r)));
    await select();
    fireEvent.click(screen.getByRole("button", { name: "Close investigation" }));
    fireEvent.click(screen.getAllByRole("button", { name: "Existing" })[0]);
    resolve({ ...A, state: "closed", version: 1 });
    await screen.findByRole("button", { name: "Reopen investigation" });
    expect(screen.getByText("Closed", { selector: "strong" })).toBeTruthy();
  });
  it("does not overwrite the board with an older version", async () => {
    let resolve!: (i: Investigation) => void;
    api.transitionInvestigation.mockReturnValue(new Promise<Investigation>((r) => (resolve = r)));
    api.listInvestigations.mockResolvedValue([{ ...A, version: 5 }]);
    await select();
    fireEvent.click(screen.getByRole("button", { name: "Close investigation" }));
    resolve({ ...A, state: "closed", version: 1 });
    await waitFor(() => expect(screen.getByRole("button", { name: "Close investigation" })).toBeTruthy());
    expect(screen.getByText("Open", { selector: "strong" })).toBeTruthy();
  });
});

describe("InvestigationBoardPage collect evidence", () => {
  const EVID = "55555555-5555-4555-8555-555555555555";
  const collected = { id: "66666666-6666-4666-8666-666666666666", evidenceVerificationId: EVID, note: null, collectedBySubject: "analyst-1", createdAtUtc: "2026-11-02T09:00:00Z" };
  const collect = async () => {
    setup();
    fireEvent.click(await screen.findByRole("button", { name: "Existing" }));
    await screen.findByLabelText("Verified evidence id");
    fireEvent.change(screen.getByLabelText("Verified evidence id"), { target: { value: EVID } });
    fireEvent.click(screen.getByRole("button", { name: "Collect evidence" }));
  };
  it("shows the server item on the board once, even when the same row is returned again", async () => {
    api.collectEvidence.mockResolvedValue(collected);
    await collect();
    await waitFor(() => expect(screen.getAllByText(EVID, { exact: false })).toHaveLength(1));
    expect(api.collectEvidence).toHaveBeenCalledWith(A.id, EVID, "");
    fireEvent.change(screen.getByLabelText("Verified evidence id"), { target: { value: EVID } });
    fireEvent.click(screen.getByRole("button", { name: "Collect evidence" }));
    await waitFor(() => expect(api.collectEvidence).toHaveBeenCalledTimes(2));
    expect(screen.getAllByText(EVID, { exact: false })).toHaveLength(1);
  });
  it("drops an answer that arrives after another investigation was opened", async () => {
    let resolve!: (i: typeof collected) => void;
    api.collectEvidence.mockReturnValue(new Promise<typeof collected>((r) => (resolve = r)));
    api.listInvestigations.mockResolvedValue([A, B]);
    await collect();
    fireEvent.click(await screen.findByRole("button", { name: "Fresh inquiry" }));
    await waitFor(() => expect(api.listEvidenceItems).toHaveBeenCalledWith(B.id));
    resolve(collected);
    await Promise.resolve();
    await waitFor(() => expect(screen.getByText("No evidence has been collected into this investigation yet.")).toBeTruthy());
    expect(screen.queryByText(EVID, { exact: false })).toBeNull();
  });
  it("keeps collecting disabled while the board is still loading", async () => {
    api.listEvidenceItems.mockReturnValue(new Promise(() => undefined));
    setup();
    fireEvent.click(await screen.findByRole("button", { name: "Existing" }));
    fireEvent.change(await screen.findByLabelText("Verified evidence id"), { target: { value: EVID } });
    expect((screen.getByRole("button", { name: "Collect evidence" }) as HTMLButtonElement).disabled).toBe(true);
    expect(api.collectEvidence).not.toHaveBeenCalled();
  });
  it("leaves the board unchanged on a refusal", async () => {
    api.collectEvidence.mockRejectedValue({ status: 404 });
    await collect();
    await screen.findByRole("alert");
    expect(screen.queryByText(EVID, { exact: false })).toBeNull();
    expect(screen.getByText("No evidence has been collected into this investigation yet.")).toBeTruthy();
  });
});
