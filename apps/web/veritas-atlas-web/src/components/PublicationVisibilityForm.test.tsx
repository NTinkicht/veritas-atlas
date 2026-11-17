// @vitest-environment jsdom
import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { MalformedRevisionResponse, type PublicationRevision } from "../api/publicationRevisions";
import { PublicationVisibilityForm } from "./PublicationVisibilityForm";

// SYNTHETIC fixtures only.
const api = { setVisibility: vi.fn() };

const rev: PublicationRevision = {
  id: "11111111-1111-4111-8111-111111111111", authorId: "analyst", contentDigest: "a".repeat(64),
  approvedDigest: "a".repeat(64), policyPermitsPublicDisplay: true, state: "Approved", version: 3, factsVersion: 2,
};
const fill = (label: string, v: string) => fireEvent.change(screen.getByLabelText(label), { target: { value: v } });

beforeEach(() => { api.setVisibility.mockReset(); });
afterEach(cleanup);

describe("PublicationVisibilityForm", () => {
  it("renders nothing unless the revision is Approved or Published", () => {
    const { container } = render(<PublicationVisibilityForm revision={{ ...rev, state: "Draft" }} onSave={api.setVisibility} signInHref="/login" />);
    expect(container.innerHTML).toBe("");
  });
  it("starts restricted (fail closed)", () => {
    render(<PublicationVisibilityForm revision={rev} onSave={api.setVisibility} signInHref="/login" />);
    expect((screen.getByLabelText(/Restricted/) as HTMLInputElement).checked).toBe(true);
  });
  it("hands the revision and facts to the owner and shows no failure on success", async () => {
    api.setVisibility.mockResolvedValue(undefined);
    const onSaved = vi.fn();
    render(<PublicationVisibilityForm revision={rev} onSave={onSaved} signInHref="/login" />);
    fireEvent.click(screen.getByLabelText(/Restricted/));
    fill("Citation label", "C1"); fill("Title", "T"); fill("Source name", "S"); fill("Freshness label", "F"); fill("Quote", "Q");
    fireEvent.click(screen.getByRole("button", { name: "Save visibility" }));
    await waitFor(() => expect(onSaved).toHaveBeenCalledTimes(1));
    const [saved, input] = onSaved.mock.calls[0];
    expect(saved).toBe(rev);
    expect(input.restricted).toBe(false);
    expect(screen.queryByRole("alert")).toBeNull();
  });
  it("shows a refusal and never claims success on 403", async () => {
    api.setVisibility.mockRejectedValue({ status: 403 });
    render(<PublicationVisibilityForm revision={rev} onSave={api.setVisibility} signInHref="/login" />);
    fireEvent.click(screen.getByRole("button", { name: "Save visibility" }));
    expect((await screen.findByRole("alert")).textContent).toMatch(/not permitted/);
  });
  it("reports a client-side validation refusal without a network claim", async () => {
    api.setVisibility.mockRejectedValue(new MalformedRevisionResponse("x"));
    render(<PublicationVisibilityForm revision={rev} onSave={api.setVisibility} signInHref="/login" />);
    fireEvent.click(screen.getByRole("button", { name: "Save visibility" }));
    expect((await screen.findByRole("alert")).textContent).toMatch(/unexpected response/);
  });
});
