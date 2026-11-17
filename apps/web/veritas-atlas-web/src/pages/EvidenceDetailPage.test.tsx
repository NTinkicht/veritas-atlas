// @vitest-environment jsdom
import { cleanup, render, screen, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { EvidenceDetail } from "../api/evidenceDetail";
import { EvidenceDetailPage } from "./EvidenceDetailPage";

// SYNTHETIC fixtures only. The network client is mocked; rendering and failure mapping are real.
const getDetail = vi.hoisted(() => vi.fn());
vi.mock("../api/evidenceDetail", async (importOriginal) => ({
  ...(await importOriginal<typeof import("../api/evidenceDetail")>()),
  getEvidenceDetail: getDetail,
}));

const REF = "ev1:synthetic/ref";
const detail = (over: Partial<EvidenceDetail> = {}): EvidenceDetail => ({
  evidenceRef: REF,
  sourceUri: "https://alpha.example/r1",
  sourceEntryId: "11111111-1111-4111-8111-111111111111",
  jurisdiction: "XX",
  sourceType: "gazette",
  attestationState: "Verified",
  retrievedAtUtc: "2026-02-03T04:05:06Z",
  freshness: "CURRENT",
  freshnessEligible: true,
  freshnessReason: null,
  freshnessEvaluatedAt: "2026-02-03T04:05:07Z",
  parserName: "p",
  parserVersion: "1",
  chunks: [{ ordinal: 0, elementId: "e0", text: "first chunk" }],
  chunksTruncated: false,
  ...over,
});
const setup = () =>
  render(
    <QueryClientProvider client={new QueryClient()}>
      <MemoryRouter initialEntries={[`/evidence/${encodeURIComponent(REF)}`]}>
        <Routes>
          <Route path="/evidence/:id" element={<EvidenceDetailPage />} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  );

beforeEach(() => {
  getDetail.mockReset();
});
afterEach(cleanup);

describe("EvidenceDetailPage", () => {
  it("requests the decoded reference and shows provenance, CURRENT badge and chunk text", async () => {
    getDetail.mockResolvedValue(detail());
    setup();
    expect(await screen.findByText("first chunk")).toBeTruthy();
    expect(getDetail).toHaveBeenCalledWith(REF);
    expect(screen.getByText("https://alpha.example/r1")).toBeTruthy();
    expect(document.querySelector("[data-freshness='CURRENT']")?.textContent).toBe("Current");
  });

  it("never presents stale evidence, or ineligible CURRENT, as current", async () => {
    getDetail.mockResolvedValue(detail({ freshness: "STALE", freshnessEligible: false, freshnessReason: "too old" }));
    setup();
    await screen.findByText("too old");
    const badge = document.querySelector("[data-freshness]")!;
    expect(badge.getAttribute("data-freshness")).toBe("STALE");
    expect(badge.className).not.toContain("es-fresh-current");
    cleanup();
    getDetail.mockResolvedValue(detail({ freshness: "CURRENT", freshnessEligible: false }));
    setup();
    await waitFor(() => expect(document.querySelector(".es-fresh-caution")?.textContent).toContain("not eligible"));
  });

  it("renders chunk markup as inert text and discloses truncation", async () => {
    getDetail.mockResolvedValue(
      detail({ chunks: [{ ordinal: 0, elementId: "e0", text: "<img src=x onerror=alert(1)>" }], chunksTruncated: true }),
    );
    const { container } = setup();
    expect(await screen.findByText("<img src=x onerror=alert(1)>")).toBeTruthy();
    expect(container.querySelector("img")).toBeNull();
    expect(screen.getByText(/bounded portion/)).toBeTruthy();
  });

  it("shows one generic message for 404 without showing any evidence", async () => {
    getDetail.mockRejectedValue({ status: 404 });
    setup();
    const alert = (await screen.findByText(/No verified evidence with that reference is available/)).closest("[role='alert']");
    expect(alert?.getAttribute("data-failure")).toBe("notfound");
    expect(screen.queryByText("Provenance")).toBeNull();
  });

  it("offers sign-in on 401 and a fixed message on 403 and malformed responses", async () => {
    getDetail.mockRejectedValue({ status: 401 });
    setup();
    expect((await screen.findByText("Sign in")).getAttribute("href")).toContain("/login?returnTo=");
    cleanup();
    getDetail.mockRejectedValue({ status: 403 });
    setup();
    expect((await screen.findByText(/not permitted to read evidence/)).closest("[role='alert']")?.getAttribute("data-failure")).toBe("forbidden");
  });
});
