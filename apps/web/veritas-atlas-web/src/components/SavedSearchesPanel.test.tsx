// @vitest-environment jsdom
import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { SavedSearch, SavedSearchDraft } from "../api/savedSearches";
import { SavedSearchesPanel } from "./SavedSearchesPanel";

// SYNTHETIC fixtures only. The network-facing client calls are mocked; validation and rendering are real.
const api = vi.hoisted(() => ({ listSavedSearches: vi.fn(), createSavedSearch: vi.fn(), deleteSavedSearch: vi.fn() }));
vi.mock("../api/savedSearches", async (importOriginal) => ({
  ...(await importOriginal<typeof import("../api/savedSearches")>()),
  ...api,
}));

const saved = (id: string, name: string, extra: Partial<SavedSearch> = {}): SavedSearch => ({
  id, name, query: "planning", freshness: "CURRENT", jurisdiction: "UK", sourceType: null, version: 1, hasUnsupportedCriteria: false, ...extra,
});
const current: SavedSearchDraft = { name: "", q: "planning", freshness: "", jurisdiction: "", type: "" };
const setup = (onApply = vi.fn(), draft = current) =>
  render(<SavedSearchesPanel current={draft} onApply={onApply} signInHref="/login" />);
const typeName = (v: string) => fireEvent.change(screen.getByLabelText("Name for the current search"), { target: { value: v } });
const saveButton = () => screen.getByRole("button", { name: "Save search" }) as HTMLButtonElement;

beforeEach(() => {
  Object.values(api).forEach((f) => f.mockReset());
  api.listSavedSearches.mockResolvedValue([]);
});
afterEach(cleanup);

describe("SavedSearchesPanel", () => {
  it("shows an honest empty state and a load refusal with a reload path", async () => {
    setup();
    expect(await screen.findByText("You have no saved searches.")).toBeTruthy();
    cleanup();
    api.listSavedSearches.mockRejectedValue({ status: 403 });
    setup();
    expect((await screen.findByRole("alert")).textContent).toContain("operator or analyst");
    expect(screen.getByRole("button", { name: "Reload saved searches" })).toBeTruthy();
  });

  it("applies a saved search through the callback", async () => {
    const s = saved("a", "Recent");
    api.listSavedSearches.mockResolvedValue([s]);
    const onApply = vi.fn();
    setup(onApply);
    fireEvent.click(await screen.findByRole("button", { name: "Apply saved search Recent" }));
    expect(onApply).toHaveBeenCalledWith(s);
  });

  it("cannot apply a saved search that uses criteria the page cannot express", async () => {
    api.listSavedSearches.mockResolvedValue([saved("a", "Parser", { hasUnsupportedCriteria: true })]);
    setup();
    await screen.findByText("Parser");
    expect(screen.queryByRole("button", { name: "Apply saved search Parser" })).toBeNull();
    expect(screen.getByText("Uses criteria this page cannot apply.")).toBeTruthy();
  });

  it("keeps Save disabled for an invalid name or an empty query and sends nothing", async () => {
    setup();
    await screen.findByText("You have no saved searches.");
    typeName(" padded");
    expect(saveButton().disabled).toBe(true);
    fireEvent.submit(screen.getByRole("form", { name: "Save current search" }));
    expect(api.createSavedSearch).not.toHaveBeenCalled();
    cleanup();
    setup(vi.fn(), { ...current, q: "  " });
    await screen.findByText("You have no saved searches.");
    typeName("Fine");
    expect(saveButton().disabled).toBe(true);
    expect(screen.getByText("Enter a query to save a search.")).toBeTruthy();
  });

  it("adds the saved search only after the server confirms and sends the current criteria", async () => {
    api.createSavedSearch.mockResolvedValue(saved("n", "New one"));
    setup(vi.fn(), { ...current, freshness: "STALE", jurisdiction: "UK" });
    await screen.findByText("You have no saved searches.");
    typeName("New one");
    expect(saveButton().disabled).toBe(false);
    fireEvent.click(saveButton());
    await waitFor(() =>
      expect(api.createSavedSearch).toHaveBeenCalledWith({ name: "New one", q: "planning", freshness: "STALE", jurisdiction: "UK", type: "" }),
    );
    expect(await screen.findByText("New one")).toBeTruthy();
    expect((screen.getByLabelText("Name for the current search") as HTMLInputElement).value).toBe("");
  });

  it("shows a duplicate-name refusal and does not add a row", async () => {
    api.createSavedSearch.mockRejectedValue({ status: 409, raw: { code: "saved_search_name_taken" } });
    setup();
    await screen.findByText("You have no saved searches.");
    typeName("Dup");
    fireEvent.click(saveButton());
    expect((await screen.findByRole("alert")).textContent).toContain("already exists");
    expect(screen.queryByText("Dup")).toBeNull();
  });

  it("removes a row only after the server confirms deletion", async () => {
    api.listSavedSearches.mockResolvedValue([saved("a", "Recent")]);
    api.deleteSavedSearch.mockRejectedValueOnce({ status: 404 });
    setup();
    fireEvent.click(await screen.findByRole("button", { name: "Delete saved search Recent" }));
    await screen.findByRole("alert");
    expect(screen.getByText("Recent")).toBeTruthy();
    api.deleteSavedSearch.mockResolvedValueOnce(undefined);
    fireEvent.click(screen.getByRole("button", { name: "Delete saved search Recent" }));
    await waitFor(() => expect(screen.queryByText("Recent")).toBeNull());
    expect(api.deleteSavedSearch).toHaveBeenLastCalledWith("a");
  });
});
