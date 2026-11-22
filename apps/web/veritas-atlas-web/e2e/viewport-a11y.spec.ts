import AxeBuilder from "@axe-core/playwright";
import { expect, test, type Page } from "@playwright/test";
import { findHorizontalOverflow, PUBLICATION_ID, PUBLICATION_URL_GLOB, publicationFixture } from "./helpers";

// VER-P0-17: the same assertions run once per viewport project (320, 390, 768, 1024, 1440 wide).

async function expectNoSeriousA11yViolations(page: Page) {
  const results = await new AxeBuilder({ page }).analyze();
  const blocking = results.violations.filter((v) => v.impact === "serious" || v.impact === "critical");
  expect(blocking.map((v) => `${v.id}: ${v.help}`)).toEqual([]);
}

async function focusOrder(page: Page, stops: number): Promise<string[]> {
  const order: string[] = [];
  for (let i = 0; i < stops; i++) {
    await page.keyboard.press("Tab");
    order.push(await page.evaluate(() => document.activeElement?.id || document.activeElement?.tagName.toLowerCase() || ""));
  }
  return order;
}

test.describe("anonymous public reader", () => {
  const route = `/public/publications/${PUBLICATION_ID}`;

  test("renders a visible publication without overflow, with ordered keyboard focus and no serious a11y violations", async ({ page }) => {
    await page.route(PUBLICATION_URL_GLOB, (r) => r.fulfill({ json: publicationFixture }));
    await page.goto(route);
    await expect(page.getByRole("heading", { level: 2, name: "Statement on the regional budget" })).toBeVisible();

    expect(await findHorizontalOverflow(page)).toEqual([]);
    // The only interactive control is the provenance link; focus reaches it first and then leaves the document.
    await page.keyboard.press("Tab");
    await expect(page.getByRole("link", { name: "Official gazette" })).toBeFocused();
    await expectNoSeriousA11yViolations(page);
  });

  test("hidden publication shows the honest unavailable label", async ({ page }) => {
    await page.route(PUBLICATION_URL_GLOB, (r) => r.fulfill({ status: 404, json: {} }));
    await page.goto(route);
    await expect(page.getByRole("status")).toHaveText("This publication is not available.");
    expect(await findHorizontalOverflow(page)).toEqual([]);
    await expectNoSeriousA11yViolations(page);
  });

  test("server error shows the honest error label", async ({ page }) => {
    await page.route(PUBLICATION_URL_GLOB, (r) => r.fulfill({ status: 500, json: {} }));
    await page.goto(route);
    await expect(page.getByRole("alert")).toHaveText("This publication could not be loaded. Please try again later.");
    expect(await findHorizontalOverflow(page)).toEqual([]);
    await expectNoSeriousA11yViolations(page);
  });

  test("offline shows the honest error label", async ({ page }) => {
    await page.route(PUBLICATION_URL_GLOB, (r) => r.abort("internetdisconnected"));
    await page.goto(route);
    await expect(page.getByRole("alert")).toHaveText("This publication could not be loaded. Please try again later.");
    expect(await findHorizontalOverflow(page)).toEqual([]);
  });

  test("empty-citation publication still renders its freshness and review labels", async ({ page }) => {
    await page.route(PUBLICATION_URL_GLOB, (r) => r.fulfill({ json: { ...publicationFixture, citations: [] } }));
    await page.goto(route);
    await expect(page.getByText("Independently reviewed by a human editor before publication.")).toBeVisible();
    await expect(page.getByText("Freshness: CURRENT")).toBeVisible();
    expect(await findHorizontalOverflow(page)).toEqual([]);
    await expectNoSeriousA11yViolations(page);
  });
});

test.describe("analyst entry route (/login)", () => {
  test("renders without overflow, focus reaches username, password then sign-in, and no serious a11y violations", async ({ page }) => {
    await page.goto("/login");
    await expect(page.getByRole("heading", { name: "Log in" })).toBeVisible();
    expect(await findHorizontalOverflow(page)).toEqual([]);
    expect(await focusOrder(page, 3)).toEqual(["username", "password", "button"]);
    await expect(page.getByRole("button", { name: "Sign in" })).toBeFocused();
    await expectNoSeriousA11yViolations(page);
  });

  test("a failed sign-in surfaces the server's error, not a silent success", async ({ page }) => {
    await page.route("**/api/v1/auth/login", (r) => r.fulfill({ status: 401, json: { message: "Invalid credentials." } }));
    await page.goto("/login");
    await page.getByLabel("Username").fill("analyst");
    await page.getByLabel("Password").fill("wrong");
    await page.getByRole("button", { name: "Sign in" }).click();
    await expect(page.getByText(/Invalid credentials|Login failed|failed/i)).toBeVisible();
    await expect(page).toHaveURL(/\/login/);
    expect(await findHorizontalOverflow(page)).toEqual([]);
  });

  test("offline sign-in stays on the page and shows an error", async ({ page }) => {
    await page.route("**/api/v1/auth/login", (r) => r.abort("internetdisconnected"));
    await page.goto("/login");
    await page.getByLabel("Username").fill("analyst");
    await page.getByLabel("Password").fill("pw");
    await page.getByRole("button", { name: "Sign in" }).click();
    await expect(page.locator(".notice-danger")).toBeVisible();
    await expect(page).toHaveURL(/\/login/);
  });
});

test.describe("overflow guard positive control", () => {
  test("the overflow check fails for a deliberately overflowing fixture", async ({ page }) => {
    await page.setContent('<main><div id="wide" style="width:3000px;height:20px;background:#ccc">wide</div></main>');
    const found = await findHorizontalOverflow(page);
    expect(found.length).toBeGreaterThan(0);
    expect(found.join("\n")).toContain("#wide");
  });

  test("the overflow check passes for a fixture that fits (negative control)", async ({ page }) => {
    await page.setContent('<main><p style="max-width:100%">fits</p></main>');
    expect(await findHorizontalOverflow(page)).toEqual([]);
  });
});
