// @vitest-environment jsdom
import { cleanup, fireEvent, render, screen } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MemoryRouter } from "react-router-dom";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import {
  InvalidAuditEventFilter,
  type AuditEvent,
} from "../api/tenantAuditEvents";
import { AuditEventsPage } from "./AuditEventsPage";

// SYNTHETIC fixtures only. The network-facing client is mocked; rendering and failure mapping are real.
const list = vi.hoisted(() => vi.fn());
vi.mock("../api/tenantAuditEvents", async (importOriginal) => ({
  ...(await importOriginal<typeof import("../api/tenantAuditEvents")>()),
  listAuditEvents: list,
}));

const event = (over: Partial<AuditEvent> = {}): AuditEvent => ({
  id: "6f1c2d3e-0000-4000-8000-000000000001",
  action: "publication.approved",
  entityType: "publication",
  entityId: "p-1",
  actorSubject: "admin",
  occurredAtUtc: "2027-01-02T03:04:05Z",
  ...over,
});

const httpError = (status: number, message = "server text") =>
  Object.assign(new Error(message), { status });

const setup = () =>
  render(
    <QueryClientProvider client={new QueryClient()}>
      <MemoryRouter initialEntries={["/audit-events?x=1"]}>
        <AuditEventsPage />
      </MemoryRouter>
    </QueryClientProvider>,
  );

beforeEach(() => list.mockReset());
afterEach(cleanup);

describe("AuditEventsPage", () => {
  it("loads unfiltered events on open and renders them as text", async () => {
    list.mockResolvedValue([event({ action: "<b>x</b>" })]);
    setup();
    expect(list).toHaveBeenCalledWith({ entityType: "", entityId: "" });
    expect(await screen.findByText("<b>x</b>")).toBeTruthy();
    expect(screen.getByText("admin")).toBeTruthy();
    expect(screen.getByRole("note").textContent).toContain("Read-only");
    expect(document.querySelector("b")).toBeNull();
  });

  it("shows an empty state when no events match", async () => {
    list.mockResolvedValue([]);
    setup();
    expect(await screen.findByText("No audit events match.")).toBeTruthy();
  });

  it("sends the entered filters on submit", async () => {
    list.mockResolvedValue([event()]);
    setup();
    await screen.findByText("publication.approved");
    fireEvent.change(screen.getByLabelText(/Entity type/), {
      target: { value: "publication" },
    });
    fireEvent.change(screen.getByLabelText(/Entity id/), {
      target: { value: "p-1" },
    });
    fireEvent.click(
      screen.getByRole("button", { name: "Filter audit events" }),
    );
    expect(list).toHaveBeenLastCalledWith({
      entityType: "publication",
      entityId: "p-1",
    });
    expect(await screen.findByText("publication.approved")).toBeTruthy();
  });

  it("shows the admin-only message on 403 without server text and no table", async () => {
    list.mockImplementationOnce(() =>
      Promise.reject(httpError(403, "leaked detail")),
    );
    setup();
    const alert = await screen.findByRole("alert");
    expect(alert.textContent).toContain("Only an organization administrator");
    expect(alert.textContent).not.toContain("leaked");
    expect(screen.queryByRole("table")).toBeNull();
  });

  it("offers a return-preserving sign-in link on 401", async () => {
    list.mockImplementationOnce(() => Promise.reject(httpError(401)));
    setup();
    const link = await screen.findByRole("link", { name: "Sign in" });
    expect(link.getAttribute("href")).toBe(
      `/login?returnTo=${encodeURIComponent("/audit-events?x=1")}`,
    );
  });

  it("shows a client-side filter refusal", async () => {
    list
      .mockResolvedValueOnce([event()])
      .mockRejectedValueOnce(
        new InvalidAuditEventFilter(
          "An entity id filter requires an entity type.",
        ),
      );
    setup();
    await screen.findByText("publication.approved");
    fireEvent.change(screen.getByLabelText(/Entity id/), {
      target: { value: "p-1" },
    });
    fireEvent.click(
      screen.getByRole("button", { name: "Filter audit events" }),
    );
    expect((await screen.findByRole("alert")).textContent).toContain(
      "requires an entity type",
    );
  });
});
