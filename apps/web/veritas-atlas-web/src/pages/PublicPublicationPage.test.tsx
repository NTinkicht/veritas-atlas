// @vitest-environment jsdom
import { act, cleanup, render, screen } from "@testing-library/react";
import { useEffect } from "react";
import { MemoryRouter, Route, Routes, useNavigate } from "react-router-dom";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { PublicPublication, PublicPublicationResult } from "../api/publicPublication";
import { ERROR_MESSAGE, HIDDEN_MESSAGE, REVIEW_STATEMENT } from "../components/PublicReaderView";
import { PublicPublicationPage } from "./PublicPublicationPage";

// SYNTHETIC fixtures only. The network client is mocked; the page's loading/state mapping is real.
const getPublication = vi.hoisted(() => vi.fn());
vi.mock("../api/publicPublication", async (importOriginal) => ({
  ...(await importOriginal<typeof import("../api/publicPublication")>()),
  getPublicPublication: getPublication,
}));

const ID_A = "11111111-1111-4111-8111-111111111111";
const ID_B = "22222222-2222-4222-8222-222222222222";

const pub = (id: string, title: string): PublicPublicationResult => {
  const publication: PublicPublication = {
    id,
    freshnessLabel: "CURRENT",
    humanReviewLabel: "independently-reviewed",
    citations: [
      { citationLabel: "S1", title, quote: "q", sourceName: "Src", asOf: "2026-11-01T00:00:00Z", effectiveDate: null, freshnessLabel: "CURRENT", contradictionDisclosure: null, uncertaintyLimit: null, sourceUri: null, confidence: null },
    ],
  };
  return { kind: "ok", publication };
};

const deferred = () => {
  let resolve!: (v: PublicPublicationResult) => void;
  const promise = new Promise<PublicPublicationResult>((r) => (resolve = r));
  return { promise, resolve };
};

let go: (to: string) => void = () => {};
function Nav() {
  const navigate = useNavigate();
  useEffect(() => {
    go = (to) => navigate(to);
  }, [navigate]);
  return null;
}

const setup = (id = ID_A) =>
  render(
    <MemoryRouter initialEntries={[`/public/publications/${id}`]}>
      <Nav />
      <Routes>
        <Route path="/public/publications/:id" element={<PublicPublicationPage />} />
      </Routes>
    </MemoryRouter>,
  );

describe("PublicPublicationPage", () => {
  beforeEach(() => getPublication.mockReset());
  afterEach(cleanup);

  it("shows loading first, then the publication with the human-review statement", async () => {
    const d = deferred();
    getPublication.mockReturnValueOnce(d.promise);
    setup();
    expect(screen.getByRole("status").textContent).toContain("Loading");
    expect(screen.queryByText(REVIEW_STATEMENT)).toBeNull();
    await act(async () => d.resolve(pub(ID_A, "Alpha title")));
    expect(screen.getByText(REVIEW_STATEMENT)).toBeTruthy();
    expect(screen.getByText(/Alpha title/)).toBeTruthy();
    expect(getPublication).toHaveBeenCalledTimes(1);
    expect(getPublication).toHaveBeenCalledWith(ID_A);
  });

  it("shows only the generic unavailable message for a hidden publication", async () => {
    getPublication.mockResolvedValueOnce({ kind: "hidden" });
    setup();
    expect(await screen.findByText(HIDDEN_MESSAGE)).toBe(screen.getByRole("status"));
    expect(screen.queryByText(REVIEW_STATEMENT)).toBeNull();
  });

  it("shows an alert and no publication content on a load error", async () => {
    getPublication.mockResolvedValueOnce({ kind: "error" });
    setup();
    expect((await screen.findByRole("alert")).textContent).toBe(ERROR_MESSAGE);
    expect(screen.queryByText(REVIEW_STATEMENT)).toBeNull();
  });

  it("never shows the previous publication after the id changes, and drops its late response", async () => {
    const a = deferred();
    const b = deferred();
    getPublication.mockReturnValueOnce(a.promise).mockReturnValueOnce(b.promise);
    setup(ID_A);
    await act(async () => go(`/public/publications/${ID_B}`));
    expect(getPublication).toHaveBeenNthCalledWith(2, ID_B);
    // The superseded id answers late: it must not render, and the page stays loading for B.
    await act(async () => a.resolve(pub(ID_A, "Alpha title")));
    expect(screen.queryByText(/Alpha title/)).toBeNull();
    expect(screen.getByRole("status").textContent).toContain("Loading");
    await act(async () => b.resolve(pub(ID_B, "Beta title")));
    expect(screen.getByText(/Beta title/)).toBeTruthy();
    expect(screen.queryByText(/Alpha title/)).toBeNull();
  });

  it("does not carry the previous publication into the loading state of the next id", async () => {
    const b = deferred();
    getPublication.mockResolvedValueOnce(pub(ID_A, "Alpha title")).mockReturnValueOnce(b.promise);
    setup(ID_A);
    expect(await screen.findByText(/Alpha title/)).toBeTruthy();
    await act(async () => go(`/public/publications/${ID_B}`));
    expect(screen.queryByText(/Alpha title/)).toBeNull();
    expect(screen.getByRole("status").textContent).toContain("Loading");
  });
});
