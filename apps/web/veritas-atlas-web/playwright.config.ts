import { defineConfig } from "@playwright/test";

// VER-P0-17: browser verification at 320/390/768/1024/desktop. API calls are stubbed per spec with
// page.route, so no backend is needed. The production build is served (it carries the CSP meta tag).
// E2E_CHROMIUM_PATH optionally points at an already-installed Chromium/Chrome binary.
const executablePath = process.env.E2E_CHROMIUM_PATH || undefined;
const PORT = 4173;

const viewports = [
  { name: "w320", width: 320, height: 640 },
  { name: "w390", width: 390, height: 844 },
  { name: "w768", width: 768, height: 1024 },
  { name: "w1024", width: 1024, height: 768 },
  { name: "w1440", width: 1440, height: 900 },
];

export default defineConfig({
  testDir: "./e2e",
  fullyParallel: true,
  forbidOnly: !!process.env.CI,
  retries: 0,
  reporter: [["list"]],
  use: { baseURL: `http://127.0.0.1:${PORT}`, launchOptions: { executablePath } },
  projects: viewports.map(({ name, width, height }) => ({
    name,
    use: { viewport: { width, height } },
  })),
  webServer: {
    command: `npx vite build && npx vite preview --host 127.0.0.1 --port ${PORT} --strictPort`,
    url: `http://127.0.0.1:${PORT}/login`,
    reuseExistingServer: !process.env.CI,
    timeout: 180_000,
  },
});
