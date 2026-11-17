// @vitest-environment jsdom
import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { CollectedEvidenceItem, Investigation } from "../api/investigations";
import { CollectEvidenceForm } from "./CollectEvidenceForm";

// SYNTHETIC fixtures only. The network-facing client call is mocked; validation and rendering are real.
const api = vi.hoisted(() => ({ collectEvidence: vi.fn() }));
vi.mock("../api/investigations", async (importOriginal) => ({ ...(await importOriginal<typeof import("../api/investigations")>()), ...api }));

const inv: Investigation = { id: "11111111-1111-4111-8111-111111111111", title: "Inquiry", createdBySubject: "analyst-1", createdAtUtc: "2026-11-02T09:00:00Z", state: "open", version: 0 };
const EV = "22222222-2222-4222-8222-222222222222";
const item: CollectedEvidenceItem = { id: "33333333-3333-4333-8333-333333333333", evidenceVerificationId: EV, note: "why", collectedBySubject: "analyst-1", createdAtUtc: "2026-11-02T09:00:00Z" };

const onCollected = vi.fn();
const setup = (investigation: Investigation = inv, ready = true) =>
  render(<CollectEvidenceForm investigation={investigation} ready={ready} signInHref="/login" onCollected={onCollected} />);
const idBox = () => screen.getByLabelText("Verified evidence id") as HTMLInputElement;
const button = () => screen.getByRole("button", { name: /Collect evidence|Collecting/ }) as HTMLButtonElement;
const fill = (id: string, note = "") => {
  fireEvent.change(idBox(), { target: { value: id } });
  fireEvent.change(screen.getByLabelText("Note (optional)"), { target: { value: note } });
};

beforeEach(() => {
  api.collectEvidence.mockReset();
  onCollected.mockReset();
});
afterEach(cleanup);

describe("CollectEvidenceForm", () => {
  it("keeps submit disabled and sends nothing for an invalid id or note", () => {
    setup();
    fill("not-a-guid");
    expect(button().disabled).toBe(true);
    fill(EV, " padded");
    expect(button().disabled).toBe(true);
    fireEvent.submit(screen.getByRole("form", { name: "Collect verified evidence" }));
    expect(api.collectEvidence).not.toHaveBeenCalled();
  });
  it("collects once, hands the server item to the board and clears the form", async () => {
    api.collectEvidence.mockResolvedValue(item);
    setup();
    fill(` ${EV} `, "why");
    fireEvent.click(button());
    await waitFor(() => expect(onCollected).toHaveBeenCalledWith(item));
    expect(api.collectEvidence).toHaveBeenCalledWith(inv.id, EV, "why");
    expect(idBox().value).toBe("");
  });
  it("sends one request for a double submit", async () => {
    let resolve!: (i: CollectedEvidenceItem) => void;
    api.collectEvidence.mockReturnValue(new Promise<CollectedEvidenceItem>((r) => (resolve = r)));
    setup();
    fill(EV);
    fireEvent.click(button());
    fireEvent.click(button());
    expect(api.collectEvidence).toHaveBeenCalledTimes(1);
    resolve(item);
    await waitFor(() => expect(onCollected).toHaveBeenCalledTimes(1));
  });
  it("shows a refusal, adds nothing and keeps the typed values", async () => {
    api.collectEvidence.mockRejectedValue({ status: 409 });
    setup();
    fill(EV, "why");
    fireEvent.click(button());
    expect((await screen.findByRole("alert")).textContent).toContain("changed or is already");
    expect(onCollected).not.toHaveBeenCalled();
    expect(idBox().value).toBe(EV);
  });
  it("offers sign-in on 401", async () => {
    api.collectEvidence.mockRejectedValue({ status: 401 });
    setup();
    fill(EV);
    fireEvent.click(button());
    await screen.findByRole("alert");
    expect(screen.getByRole("link").getAttribute("href")).toBe("/login");
  });
  it("is disabled for a closed investigation and explains why", () => {
    setup({ ...inv, state: "closed" });
    fill(EV);
    expect(button().disabled).toBe(true);
    expect(screen.getByText("Reopen the investigation to collect evidence.")).toBeTruthy();
    fireEvent.submit(screen.getByRole("form", { name: "Collect verified evidence" }));
    expect(api.collectEvidence).not.toHaveBeenCalled();
  });
  it("is disabled until the collected evidence has loaded", () => {
    setup(inv, false);
    fill(EV);
    expect(button().disabled).toBe(true);
    expect(screen.getByRole("status").textContent).toContain("once the collected evidence has loaded");
    fireEvent.submit(screen.getByRole("form", { name: "Collect verified evidence" }));
    expect(api.collectEvidence).not.toHaveBeenCalled();
  });
});
