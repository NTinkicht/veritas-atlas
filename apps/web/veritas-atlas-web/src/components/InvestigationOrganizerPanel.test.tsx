// @vitest-environment jsdom
import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { Investigation } from "../api/investigations";
import { InvestigationOrganizerPanel } from "./InvestigationOrganizerPanel";

// SYNTHETIC fixtures only. The network-facing client calls are mocked; validation and rendering are real.
const api = vi.hoisted(() => ({ listTags: vi.fn(), addTag: vi.fn(), removeTag: vi.fn(), getFolder: vi.fn(), setFolder: vi.fn(), clearFolder: vi.fn() }));
vi.mock("../api/investigationOrganizer", async (importOriginal) => ({
  ...(await importOriginal<typeof import("../api/investigationOrganizer")>()),
  ...api,
}));

const A: Investigation = { id: "11111111-1111-4111-8111-111111111111", title: "A", createdBySubject: "analyst-1", createdAtUtc: "2026-11-02T09:00:00Z", state: "open", version: 0 };
const B: Investigation = { ...A, id: "22222222-2222-4222-8222-222222222222", title: "B" };
const setup = (inv: Investigation = A) => render(<InvestigationOrganizerPanel investigation={inv} signInHref="/login" />);
const typeTag = (v: string) => fireEvent.change(screen.getByLabelText("Tag (lowercase letters, digits, _ . -)"), { target: { value: v } });
const typeFolder = (v: string) => fireEvent.change(screen.getByLabelText("Folder name"), { target: { value: v } });
const addButton = () => screen.getByRole("button", { name: "Add tag" }) as HTMLButtonElement;
const folderButton = () => screen.getByRole("button", { name: "Set folder" }) as HTMLButtonElement;

beforeEach(() => {
  Object.values(api).forEach((f) => f.mockReset());
  api.listTags.mockResolvedValue([]);
  api.getFolder.mockResolvedValue(null);
});
afterEach(cleanup);

describe("InvestigationOrganizerPanel", () => {
  it("shows the saved tags and folder, and honest empty states", async () => {
    api.listTags.mockResolvedValue(["alpha", "beta"]);
    api.getFolder.mockResolvedValue("Case files");
    setup();
    expect(await screen.findByText("alpha")).toBeTruthy();
    expect(screen.getByText("beta")).toBeTruthy();
    expect(screen.getByText("Case files")).toBeTruthy();
    cleanup();
    api.listTags.mockResolvedValue([]);
    api.getFolder.mockResolvedValue(null);
    setup();
    expect(await screen.findByText("You have not tagged this investigation.")).toBeTruthy();
    expect(screen.getByText("Not filed in a folder.")).toBeTruthy();
  });

  it("shows a load refusal with a reload path, and nothing is invented", async () => {
    api.listTags.mockRejectedValue({ status: 500 });
    setup();
    expect((await screen.findByRole("alert")).textContent).toContain("HTTP 500");
    expect(screen.queryByText("Not filed in a folder.")).toBeNull();
    api.listTags.mockResolvedValue(["alpha"]);
    fireEvent.click(screen.getByRole("button", { name: "Reload tags and folder" }));
    expect(await screen.findByText("alpha")).toBeTruthy();
  });

  it("keeps Add tag disabled and sends nothing for an invalid tag, and enables it for a valid one", async () => {
    setup();
    await screen.findByText("You have not tagged this investigation.");
    typeTag("has space");
    expect(addButton().disabled).toBe(true);
    fireEvent.submit(screen.getByRole("form", { name: "Add a tag" }));
    expect(api.addTag).not.toHaveBeenCalled();
    typeTag("Planning");
    expect(addButton().disabled).toBe(false);
  });

  it("adds the lowercased tag only after the server confirms, and a replay is not shown twice", async () => {
    api.listTags.mockResolvedValue(["planning"]);
    api.addTag.mockResolvedValue("planning");
    setup();
    await screen.findByText("planning");
    typeTag("Planning");
    fireEvent.click(addButton());
    await waitFor(() => expect(api.addTag).toHaveBeenCalledWith(A.id, "planning"));
    await waitFor(() => expect((screen.getByLabelText("Tag (lowercase letters, digits, _ . -)") as HTMLInputElement).value).toBe(""));
    expect(screen.getAllByText("planning")).toHaveLength(1);
  });

  it("shows a refused add and leaves the list and the typed value alone", async () => {
    api.addTag.mockRejectedValue({ status: 409, raw: { code: "tag_limit_reached" } });
    setup();
    await screen.findByText("You have not tagged this investigation.");
    typeTag("extra");
    fireEvent.click(addButton());
    expect((await screen.findByRole("alert")).textContent).toContain("tag limit");
    expect(screen.queryByText("extra", { selector: "span" })).toBeNull();
    expect((screen.getByLabelText("Tag (lowercase letters, digits, _ . -)") as HTMLInputElement).value).toBe("extra");
  });

  it("removes a tag only after the server confirms and keeps it on refusal", async () => {
    api.listTags.mockResolvedValue(["alpha"]);
    api.removeTag.mockRejectedValueOnce({ status: 403 });
    setup();
    await screen.findByText("alpha");
    fireEvent.click(screen.getByRole("button", { name: "Remove tag alpha" }));
    expect((await screen.findByRole("alert")).textContent).toContain("operator or analyst");
    expect(screen.getByText("alpha")).toBeTruthy();
    api.removeTag.mockResolvedValueOnce(undefined);
    fireEvent.click(screen.getByRole("button", { name: "Remove tag alpha" }));
    await waitFor(() => expect(screen.queryByText("alpha")).toBeNull());
    expect(api.removeTag).toHaveBeenCalledWith(A.id, "alpha");
  });

  it("sets and clears the folder from server answers, validating the name first", async () => {
    api.setFolder.mockResolvedValue("Case files");
    api.clearFolder.mockResolvedValue(undefined);
    setup();
    await screen.findByText("Not filed in a folder.");
    typeFolder("bad/name");
    expect(folderButton().disabled).toBe(true);
    typeFolder("  Case files ");
    expect(folderButton().disabled).toBe(false);
    fireEvent.click(folderButton());
    await waitFor(() => expect(api.setFolder).toHaveBeenCalledWith(A.id, "Case files"));
    expect(await screen.findByText("Case files")).toBeTruthy();
    fireEvent.click(screen.getByRole("button", { name: "Remove from folder Case files" }));
    expect(await screen.findByText("Not filed in a folder.")).toBeTruthy();
    expect(api.clearFolder).toHaveBeenCalledWith(A.id);
  });

  it("disables every change and sends nothing for a closed investigation, while still listing tags", async () => {
    api.listTags.mockResolvedValue(["alpha"]);
    api.getFolder.mockResolvedValue("Case files");
    setup({ ...A, state: "closed" });
    await screen.findByText("alpha");
    expect((screen.getByRole("button", { name: "Remove tag alpha" }) as HTMLButtonElement).disabled).toBe(true);
    expect((screen.getByRole("button", { name: "Remove from folder Case files" }) as HTMLButtonElement).disabled).toBe(true);
    typeTag("beta");
    expect(addButton().disabled).toBe(true);
    fireEvent.submit(screen.getByRole("form", { name: "Add a tag" }));
    typeFolder("Other");
    fireEvent.submit(screen.getByRole("form", { name: "File in a folder" }));
    expect(api.addTag).not.toHaveBeenCalled();
    expect(api.setFolder).not.toHaveBeenCalled();
  });

  it("shows the server's closed refusal when it disagrees with the local state", async () => {
    api.addTag.mockRejectedValue({ status: 409, raw: { code: "investigation_closed" } });
    setup();
    await screen.findByText("You have not tagged this investigation.");
    typeTag("beta");
    fireEvent.click(addButton());
    expect((await screen.findByRole("alert")).textContent).toContain("is closed");
  });

  it("starts fresh and drops a late answer when another investigation is opened", async () => {
    let resolveA: (v: string[]) => void = () => {};
    api.listTags.mockImplementation((id: string) => (id === A.id ? new Promise((r) => (resolveA = r)) : Promise.resolve(["only-b"])));
    const view = setup(A);
    view.rerender(<InvestigationOrganizerPanel investigation={B} signInHref="/login" />);
    expect(await screen.findByText("only-b")).toBeTruthy();
    resolveA(["only-a"]);
    await Promise.resolve();
    expect(screen.queryByText("only-a")).toBeNull();
  });
});
