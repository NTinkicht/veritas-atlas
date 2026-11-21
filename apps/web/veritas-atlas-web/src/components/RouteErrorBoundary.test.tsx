// @vitest-environment jsdom
import { cleanup, render, screen } from "@testing-library/react";
import { createMemoryRouter, RouterProvider, type RouteObject } from "react-router-dom";
import { afterEach, describe, expect, it, vi } from "vitest";
import { CRASH_MESSAGE, NOT_FOUND_MESSAGE, RouteErrorBoundary } from "./RouteErrorBoundary";
import { buildAppRoutes } from "../routes";

afterEach(cleanup);

const SECRET = "SECRET-TOKEN-abc123 at /srv/internal/Thing.tsx:42";
function Boom(): never {
  throw new Error(SECRET);
}
function Fine() {
  return <p>fine page</p>;
}
const wrap = (children: RouteObject[]): RouteObject[] => [{ errorElement: <RouteErrorBoundary />, children }];

function mount(routes: RouteObject[], url: string) {
  vi.spyOn(console, "error").mockImplementation(() => {});
  return render(<RouterProvider router={createMemoryRouter(routes, { initialEntries: [url] })} />);
}

describe("RouteErrorBoundary", () => {
  it("renders the page normally when nothing throws (positive control)", () => {
    mount(wrap([{ path: "/ok", element: <Fine /> }]), "/ok");
    expect(screen.getByText("fine page")).toBeTruthy();
    expect(screen.queryByRole("alert")).toBeNull();
  });

  it("shows only the generic notice when a page throws, never the message or stack", () => {
    const { container } = mount(wrap([{ path: "/boom", element: <Boom /> }]), "/boom");
    expect(screen.getByRole("alert").textContent).toBe(CRASH_MESSAGE);
    expect(container.textContent).not.toContain("SECRET-TOKEN");
    expect(container.textContent).not.toContain("/srv/internal");
    expect(container.textContent).not.toContain("Unexpected Application Error");
    expect(screen.getByRole("heading", { level: 1 }).textContent).toBe("Something went wrong");
  });

  it("shows a not-found notice for an unmatched URL", () => {
    mount(wrap([{ path: "/ok", element: <Fine /> }]), "/nope");
    expect(screen.getByRole("alert").textContent).toBe(NOT_FOUND_MESSAGE);
  });

  it("is wired into the production app routes: unmatched URL is caught by the boundary", () => {
    mount(buildAppRoutes(false), "/definitely/not/a/route");
    expect(screen.getByRole("alert").textContent).toBe(NOT_FOUND_MESSAGE);
  });
});
