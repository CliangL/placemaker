// Playwright suite for the browser mock (run via `npm run test:ui`, which starts the mock host and
// sets PM_MOCK_URL). Screenshots for review land in mock/screenshots/.
const { defineConfig, devices } = require("@playwright/test");

module.exports = defineConfig({
    testDir: "./mock/tests",
    timeout: 45_000,
    expect: { timeout: 8_000 },
    fullyParallel: false,
    workers: 1,
    reporter: [["list"]],
    use: {
        baseURL: process.env.PM_MOCK_URL || "http://127.0.0.1:5123/",
        ...devices["Desktop Chrome"],
        viewport: { width: 1920, height: 1080 },
        deviceScaleFactor: 1,
    },
});
