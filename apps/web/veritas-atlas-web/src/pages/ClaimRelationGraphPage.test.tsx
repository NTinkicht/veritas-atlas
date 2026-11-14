// @vitest-environment jsdom
import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MemoryRouter } from "react-router-dom";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { RelationGraph } from "../api/claimRelationGraph";
import { ClaimRelationGraphPage } from "./ClaimRelationGraphPage";

// SYNTHETIC fixtures only. The network-facing builder is mocked; classification and rendering are real.
const build = vi.hoisted(() => vi.fn());
vi.mock("../api/claimRelationGraph", async (importOriginal) => ({
  ...(await importOriginal<typeof import("../api/claimRelationGraph")>()),
  buildRelationGraph: build,
}));

const graph: RelationGraph = {
  nodes: [{ claimId: "a", evidenceRef: "ev1:a", sourceId: null, status: "agreeing" }],
  edges: [],
};
const setup = () =>
  render(
    <QueryClientProvider client={new QueryClient()}>
    <MemoryRouter initialEntries={["/claim-relations?x=1"]}>
      <ClaimRelationGraphPage />
    </MemoryRouter>
    </QueryClientProvider>,
  );

beforeEach(() => build.mockReset());
afterEach(cleanup);

describe("ClaimRelationGraphPage", () => {
  it("starts with two claim forms and no result, and can add and remove claims", () => {
    setup();
    expect(screen.getAllByLabelText(/Claim id/)).toHaveLength(2);
    fireEvent.click(screen.getByRole("button", { name: "Add claim" }));
    expect(screen.getAllByLabelText(/Claim id/)).toHaveLength(3);
    fireEvent.click(screen.getByRole("button", { name: "Remove claim 3" }));
    expect(screen.getAllByLabelText(/Claim id/)).toHaveLength(2);
    expect(screen.queryByText(/Claims \(/)).toBeNull();
    expect(build).not.toHaveBeenCalled();
  });

  it("submits entered fields and shows the server graph", async () => {
    build.mockResolvedValue(graph);
    setup();
    fireEvent.change(screen.getAllByLabelText(/Claim id/)[0], { target: { value: "a" } });
    fireEvent.click(screen.getByRole("button", { name: "Compare claims" }));
    expect(build.mock.calls[0][0][0]).toMatchObject({ claimId: "a" });
    expect(await screen.findByText("Claims (1)")).toBeTruthy();
  });

  it("shows an honest error with sign-in on 401 and no graph", async () => {
    build.mockImplementationOnce(() => Promise.reject({ status: 401 }));
    setup();
    fireEvent.click(screen.getByRole("button", { name: "Compare claims" }));
    const alert = await screen.findByRole("alert");
    expect(alert.textContent).toContain("Sign in again");
    expect(alert.querySelector("a")?.getAttribute("href")).toBe("/login?returnTo=%2Fclaim-relations%3Fx%3D1");
    expect(screen.queryByText(/Claims \(/)).toBeNull();
  });

  it("ignores a stale response when a newer request was submitted", async () => {
    let first!: (g: RelationGraph) => void;
    build.mockReturnValueOnce(new Promise<RelationGraph>((r) => (first = r)));
    build.mockResolvedValueOnce({ nodes: [{ claimId: "new", evidenceRef: "ev1:n", sourceId: null, status: "isolated" }], edges: [] });
    setup();
    const go = () => fireEvent.click(screen.getByRole("button", { name: "Compare claims" }));
    go();
    go();
    await waitFor(() => expect(screen.getByText(/new: No relations/)).toBeTruthy());
    first(graph);
    await Promise.resolve();
    expect(screen.queryByText(/a: Agreeing/)).toBeNull();
  });
});
