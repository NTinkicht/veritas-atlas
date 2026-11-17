// @vitest-environment jsdom
import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { MalformedRevisionResponse, type PublicationRevision } from "../api/publicationRevisions";
import { PublicationVisibilityForm } from "./PublicationVisibilityForm";

// SYNTHETIC fixtures only.
const api = vi.hoisted(() => ({ setVisibility: vi.fn() }));
vi.mock("../api/publicationVisibility", async (importOriginal) => ({
  ...(await importOriginal<typeof import("../api/publicationVisibility")>()),
  ...api,
}));

const rev: PublicationRevision = {
  id: "11111111-1111-4111-8111-111111111111", authorId: "analyst", contentDigest: "a".repeat(64),
  approvedDigest: "a".repeat(64), policyPermitsPublicDisplay: true, state: "Approved", version: 3, factsVersion: 2,
};
const fill = (label: string, v: string) => fireEvent.change(screen.getByLabelText(label), { target: { value: v } });

beforeEach(() => { api.setVisibility.mockReset(); });
afterEach(cleanup);

describe("PublicationVisibilityForm", () => {
  it("renders nothing unless the revision is Approved or Published", () => {
    const { container } = render(<PublicationVisibilityForm revision={{ ...rev, state: "Draft" }} onSaved={() => {}} signInHref="/login" />);
    expect(container.innerHTML).toBe("");
  });
  it("starts restricted (fail closed)", () => {
    render(<PublicationVisibilityForm revision={rev} onSaved={() => {}} signInHref="/login" />);
    expect((screen.getByLabelText(/Restricted/) as HTMLInputElement).checked).toBe(true);
  });
  it("sends the facts version and reports success only after the server accepts", async () => {
    api.setVisibility.mockResolvedValue({ replayed: false, revision: rev });
    const onSaved = vi.fn();
    render(<PublicationVisibilityForm revision={rev} onSaved={onSaved} signInHref="/login" />);
    fireEvent.click(screen.getByLabelText(/Restricted/));
    fill("Citation label", "C1"); fill("Title", "T"); fill("Source name", "S"); fill("Freshness label", "F"); fill("Quote", "Q");
    fireEvent.click(screen.getByRole("button", { name: "Save visibility" }));
    await waitFor(() => expect(screen.getByRole("status").textContent).toMatch(/saved by the server/));
    const [id, input, version] = api.setVisibility.mock.calls[0];
    expect(id).toBe(rev.id);
    expect(version).toBe(2);
    expect(input.restricted).toBe(false);
    expect(onSaved).toHaveBeenCalledWith(rev.id);
  });
  it("shows a refusal and never claims success on 403", async () => {
    api.setVisibility.mockRejectedValue({ status: 403 });
    const onSaved = vi.fn();
    render(<PublicationVisibilityForm revision={rev} onSaved={onSaved} signInHref="/login" />);
    fireEvent.click(screen.getByRole("button", { name: "Save visibility" }));
    expect((await screen.findByRole("alert")).textContent).toMatch(/not permitted/);
    expect(screen.queryByRole("status")).toBeNull();
    expect(onSaved).not.toHaveBeenCalled();
  });
  it("reports a client-side validation refusal without a network claim", async () => {
    api.setVisibility.mockRejectedValue(new MalformedRevisionResponse("x"));
    render(<PublicationVisibilityForm revision={rev} onSaved={() => {}} signInHref="/login" />);
    fireEvent.click(screen.getByRole("button", { name: "Save visibility" }));
    expect((await screen.findByRole("alert")).textContent).toMatch(/unexpected response/);
  });
});
