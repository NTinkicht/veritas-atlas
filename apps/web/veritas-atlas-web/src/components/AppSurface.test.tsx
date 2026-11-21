// @vitest-environment jsdom
import { cleanup, render as rtlRender } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { afterEach, describe, expect, it, vi } from "vitest";

vi.mock("./UtilityHeader", () => ({ UtilityHeader: () => null }));

import { AppSurface } from "./AppSurface";

const links = [
  { href: "/", label: "Home" },
  { href: "/evidence-search", label: "Evidence Search" },
  { href: "/investigations", label: "Investigations" },
];

afterEach(cleanup);

function render(path: string) {
  const { container } = rtlRender(
    <MemoryRouter initialEntries={[path]}>
      <AppSurface title="Page title" links={links}>
        <p>body</p>
      </AppSurface>
    </MemoryRouter>,
  );
  return container;
}

describe("AppSurface landmarks", () => {
  it("puts a skip link first, targeting an existing main landmark", () => {
    const doc = render("/evidence-search");
    const first = doc.querySelector("a, button, input, select, textarea");
    expect(first?.className).toBe("skip-link");
    expect(first?.getAttribute("href")).toBe("#main-content");
    const main = doc.querySelector("#main-content");
    expect(main?.tagName).toBe("MAIN");
    expect(main?.getAttribute("tabindex")).toBe("-1");
  });

  it("names the primary navigation", () => {
    const nav = render("/").querySelector("nav");
    expect(nav?.getAttribute("aria-label")).toBe("Primary");
  });

  it("marks exactly the active link with aria-current=page", () => {
    const doc = render("/investigations");
    const current = Array.from(doc.querySelectorAll("[aria-current]"));
    expect(current).toHaveLength(1);
    expect(current[0].textContent).toBe("Investigations");
    expect(current[0].getAttribute("aria-current")).toBe("page");
  });

  it("marks Home only on the root path, not on every route", () => {
    const doc = render("/");
    const current = Array.from(doc.querySelectorAll("[aria-current]"));
    expect(current.map((e) => e.textContent)).toEqual(["Home"]);
  });

  it("exposes exactly one level-1 heading equal to the page title", () => {
    const h1s = Array.from(render("/").querySelectorAll("h1"));
    expect(h1s.map((h) => h.textContent)).toEqual(["Page title"]);
  });
});
