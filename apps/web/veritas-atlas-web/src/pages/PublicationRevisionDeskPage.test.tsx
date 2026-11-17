// @vitest-environment jsdom
import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MemoryRouter } from "react-router-dom";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { PublicationRevision } from "../api/publicationRevisions";
import { PublicationRevisionDeskPage } from "./PublicationRevisionDeskPage";

// SYNTHETIC fixtures only. Network-facing clients are mocked; the desk wiring is real.
const api = vi.hoisted(() => ({ getRevision: vi.fn(), listRevisions: vi.fn(), setVisibility: vi.fn() }));
vi.mock("../api/publicationRevisions", async (o) => ({ ...(await o<typeof import("../api/publicationRevisions")>()), getRevision: api.getRevision, listRevisions: api.listRevisions }));
vi.mock("../api/publicationVisibility", async (o) => ({ ...(await o<typeof import("../api/publicationVisibility")>()), setVisibility: api.setVisibility }));

const A: PublicationRevision = { id: "11111111-1111-4111-8111-111111111111", authorId: "a", contentDigest: "a".repeat(64), approvedDigest: "a".repeat(64), policyPermitsPublicDisplay: true, state: "Approved", version: 3, factsVersion: 0 };
const B: PublicationRevision = { ...A, id: "22222222-2222-4222-8222-222222222222" };
const open = async (r: PublicationRevision) => {
  fireEvent.change(screen.getByLabelText("Revision id"), { target: { value: r.id } });
  fireEvent.click(screen.getByRole("button", { name: "Open" }));
  await screen.findByText(`Revision ${r.id}`);
};
const save = () => fireEvent.click(screen.getByRole("button", { name: "Save visibility" }));

beforeEach(() => {
  Object.values(api).forEach((f) => f.mockReset());
  api.listRevisions.mockResolvedValue({ items: [], nextAfterId: null });
  api.getRevision.mockImplementation(async (id: string) => (id === A.id ? A : B));
});
afterEach(cleanup);
const setup = () => render(<QueryClientProvider client={new QueryClient()}><MemoryRouter><PublicationRevisionDeskPage /></MemoryRouter></QueryClientProvider>);

describe("PublicationRevisionDeskPage visibility save", () => {
  it("keeps the confirmation after the reload that unmounts the form", async () => {
    api.setVisibility.mockResolvedValue({ replayed: false, revision: A });
    setup();
    await open(A);
    save();
    expect((await screen.findByText("Public visibility saved by the server.")).textContent).toBeTruthy();
    expect(api.getRevision).toHaveBeenCalledTimes(2);
  });
  it("drops a late save for revision A once revision B was opened", async () => {
    let finish!: () => void;
    api.setVisibility.mockImplementation(() => new Promise((res) => { finish = () => res({ replayed: false, revision: A }); }));
    setup();
    await open(A);
    save();
    await open(B);
    finish();
    await waitFor(() => expect(api.setVisibility).toHaveBeenCalledTimes(1));
    await new Promise((r) => setTimeout(r, 20));
    expect(screen.queryByText("Public visibility saved by the server.")).toBeNull();
    expect(screen.getByText(`Revision ${B.id}`)).toBeTruthy();
    expect(api.getRevision).toHaveBeenCalledTimes(2);
  });
});
