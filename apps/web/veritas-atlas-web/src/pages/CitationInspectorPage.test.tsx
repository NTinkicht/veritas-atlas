// @vitest-environment jsdom
import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MemoryRouter } from "react-router-dom";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { InvalidCitationInput, type CitationVerdict } from "../api/citationInspector";
import { CitationInspectorPage } from "./CitationInspectorPage";

// SYNTHETIC fixtures only. The network-facing inspector is mocked; classification and rendering are real.
const inspect = vi.hoisted(() => vi.fn());
vi.mock("../api/citationInspector", async (importOriginal) => ({
  ...(await importOriginal<typeof import("../api/citationInspector")>()),
  inspectCitations: inspect,
}));

type Deferred = { promise: Promise<CitationVerdict>; resolve: (v: CitationVerdict) => void; reject: (e: unknown) => void };
function deferred(): Deferred {
  let resolve!: Deferred["resolve"];
  let reject!: Deferred["reject"];
  const promise = new Promise<CitationVerdict>((res, rej) => {
    resolve = res;
    reject = rej;
  });
  return { promise, resolve, reject };
}
const okVerdict: CitationVerdict = { valid: true, code: null, items: [{ evidenceRef: "ev1:a", valid: true, code: null, freshness: "CURRENT", evaluatedAt: null, historicalAsOf: null }] };

function setup() {
  render(
    <QueryClientProvider client={new QueryClient()}>
      <MemoryRouter initialEntries={["/citation-inspector?x=1"]}>
        <CitationInspectorPage />
      </MemoryRouter>
    </QueryClientProvider>,
  );
}
const submit = () => fireEvent.click(screen.getByRole("button", { name: "Inspect citations" }));

beforeEach(() => inspect.mockReset());
afterEach(cleanup);

describe("CitationInspectorPage", () => {
  it("renders the form with no verdict before any submission", () => {
    setup();
    expect(screen.getByLabelText(/Evidence reference 1/)).toBeTruthy();
    expect(screen.queryByText(/Citations (accepted|rejected)/)).toBeNull();
    expect(inspect).not.toHaveBeenCalled();
  });

  it("submits the policy and rows, shows loading, then the server verdict", async () => {
    const d = deferred();
    inspect.mockReturnValue(d.promise);
    setup();
    fireEvent.change(screen.getByLabelText(/Evidence reference 1/), { target: { value: "ev1:a" } });
    fireEvent.click(screen.getByLabelText(/Historical \(non-current\)/));
    submit();
    expect(inspect).toHaveBeenCalledWith(
      { evidenceRequired: true, historicalEvidenceAllowed: true, historicalTimeContextPreserved: false },
      [{ evidenceRef: "ev1:a", parserName: "", parserVersion: "", scopeSourceEntryId: "" }],
    );
    expect(screen.getByRole("status").textContent).toContain("Inspecting");
    expect(screen.queryByText("Citations accepted")).toBeNull();
    d.resolve(okVerdict);
    expect(await screen.findByText("Citations accepted")).toBeTruthy();
  });

  it("adds and removes citation rows within the limit", () => {
    setup();
    fireEvent.click(screen.getByRole("button", { name: "Add citation" }));
    expect(screen.getByLabelText(/Evidence reference 2/)).toBeTruthy();
    fireEvent.click(screen.getByRole("button", { name: "Remove citation 2" }));
    expect(screen.queryByLabelText(/Evidence reference 2/)).toBeNull();
    for (let i = 0; i < 25; i++) {
      const add = screen.getByRole("button", { name: "Add citation" }) as HTMLButtonElement;
      if (!add.disabled) fireEvent.click(add);
    }
    expect(screen.getAllByLabelText(/Evidence reference \d+/)).toHaveLength(20);
    expect((screen.getByRole("button", { name: "Add citation" }) as HTMLButtonElement).disabled).toBe(true);
  });

  it("ignores a stale response when a newer submission is in flight", async () => {
    const first = deferred();
    const second = deferred();
    inspect.mockReturnValueOnce(first.promise).mockReturnValueOnce(second.promise);
    setup();
    submit();
    submit();
    second.resolve({ valid: false, code: "EVIDENCE_REQUIRED", items: [] });
    expect(await screen.findByText("Citations rejected")).toBeTruthy();
    first.resolve(okVerdict);
    await new Promise((r) => setTimeout(r, 0));
    expect(screen.queryByText("Citations accepted")).toBeNull();
    expect(screen.getByText("Citations rejected")).toBeTruthy();
  });

  it("shows invalid input, auth and offline failures honestly and no verdict", async () => {
    inspect.mockRejectedValueOnce(new InvalidCitationInput("Citation 1 needs an evidence reference."));
    setup();
    submit();
    expect((await screen.findByRole("alert")).textContent).toContain("needs an evidence reference");
    inspect.mockRejectedValueOnce({ status: 401 });
    submit();
    await waitFor(() => expect(screen.getByRole("alert").textContent).toContain("Sign in"));
    expect(screen.getByRole("link", { name: "Sign in" }).getAttribute("href")).toBe("/login?returnTo=%2Fcitation-inspector%3Fx%3D1");
    inspect.mockRejectedValueOnce(new TypeError("fetch failed"));
    submit();
    await waitFor(() => expect(screen.getByRole("alert").textContent).toContain("could not be reached"));
    expect(screen.queryByText(/Citations (accepted|rejected)/)).toBeNull();
  });
});
