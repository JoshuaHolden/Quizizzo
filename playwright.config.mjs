import { defineConfig, devices } from "@playwright/test";

const baseURL = process.env.QUIZIZZO_BASE_URL ?? "http://localhost:8081";
const channel = process.env.PLAYWRIGHT_CHANNEL ?? (process.platform === "win32" ? "msedge" : undefined);

export default defineConfig({
    testDir: "./tests/browser",
    outputDir: "./artifacts/playwright-results",
    fullyParallel: false,
    workers: Number(process.env.PLAYWRIGHT_WORKERS ?? "1"),
    forbidOnly: Boolean(process.env.CI),
    retries: process.env.CI ? 2 : 0,
    reporter: [["list"], ["html", { outputFolder: "artifacts/playwright-report", open: "never" }]],
    use: {
        baseURL,
        channel,
        colorScheme: "light",
        ignoreHTTPSErrors: false,
        screenshot: "only-on-failure",
        trace: "retain-on-failure",
    },
    projects: [
        {
            name: "desktop-edge",
            use: {
                ...devices["Desktop Edge"],
                viewport: { width: 1440, height: 900 },
            },
        },
        {
            name: "tablet-edge",
            use: {
                ...devices["Desktop Edge"],
                hasTouch: true,
                viewport: { width: 768, height: 1024 },
            },
        },
        {
            name: "phone-edge",
            use: {
                ...devices["Desktop Edge"],
                hasTouch: true,
                isMobile: true,
                viewport: { width: 320, height: 568 },
            },
        },
    ],
});
