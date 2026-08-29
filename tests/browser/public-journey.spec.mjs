import { expect, test } from "@playwright/test";

const publicPages = [
    { name: "home", path: "/", heading: /big-screen chaos/i },
    { name: "join", path: "/join", heading: /join the party/i },
    { name: "display", path: "/display", heading: /Quizizzo|display/i },
    { name: "player", path: "/play", heading: /no active player/i },
    { name: "login", path: "/Account/Login", heading: /log in/i },
    { name: "register", path: "/Account/Register", heading: /register|create/i },
];

for (const publicPage of publicPages) {
    test(`${publicPage.name} is clean and usable`, async ({ page }, testInfo) => {
        const browserErrors = [];
        const failedRequests = [];

        page.on("console", message => {
            if (message.type() === "error") {
                browserErrors.push(`console: ${message.text()}`);
            }
        });
        page.on("pageerror", error => browserErrors.push(`page: ${error.message}`));
        page.on("requestfailed", request => {
            if (!request.isNavigationRequest() || request.failure()?.errorText !== "net::ERR_ABORTED") {
                failedRequests.push(`${request.method()} ${request.url()}: ${request.failure()?.errorText ?? "failed"}`);
            }
        });

        let response;
        for (let attempt = 0; attempt < 2; attempt += 1) {
            try {
                response = await page.goto(publicPage.path, { waitUntil: "networkidle" });
                break;
            } catch (error) {
                if (attempt > 0 || !String(error).includes("ERR_NETWORK_CHANGED")) {
                    throw error;
                }
            }
        }
        expect(response, "navigation should return a response").not.toBeNull();
        expect(response?.ok(), `HTTP ${response?.status()} for ${publicPage.path}`).toBeTruthy();
        await expect(page.locator("h1").first()).toContainText(publicPage.heading);

        const pageAudit = await page.evaluate(() => {
            const duplicateIds = [...document.querySelectorAll("[id]")]
                .map(element => element.id)
                .filter((id, index, ids) => id && ids.indexOf(id) !== index);
            const unlabeledControls = [...document.querySelectorAll("button, input, select, textarea")]
                .filter(element => element.getAttribute("type") !== "hidden")
                .filter(element => {
                    const text = element.textContent?.trim();
                    const ariaLabel = element.getAttribute("aria-label");
                    const title = element.getAttribute("title");
                    const labelledBy = element.getAttribute("aria-labelledby");
                    const label = element.closest("label") ||
                        (element.id && document.querySelector(`label[for="${CSS.escape(element.id)}"]`));
                    return !text && !ariaLabel && !title && !labelledBy && !label;
                })
                .map(element => element.outerHTML);
            const vaguePrimaryActions = [...document.querySelectorAll(".btn-primary")]
                .map(element => element.textContent?.replace(/\s+/g, " ").trim() ?? "")
                .filter(label => /^(submit|continue|confirm|save|reset|resend|verify|download|register)$/i.test(label));

            return {
                duplicateIds: [...new Set(duplicateIds)],
                unlabeledControls,
                vaguePrimaryActions,
                horizontalOverflow: document.documentElement.scrollWidth - window.innerWidth,
            };
        });

        await page.screenshot({
            path: testInfo.outputPath(`${publicPage.name}.png`),
            fullPage: true,
            animations: "disabled",
        });

        expect.soft(browserErrors, "browser errors").toEqual([]);
        expect.soft(failedRequests, "failed requests").toEqual([]);
        expect.soft(pageAudit.duplicateIds, "duplicate element IDs").toEqual([]);
        expect.soft(pageAudit.unlabeledControls, "unlabeled form controls").toEqual([]);
        expect.soft(pageAudit.vaguePrimaryActions, "vague primary action labels").toEqual([]);
        expect.soft(pageAudit.horizontalOverflow, "document overflow in CSS pixels").toBeLessThanOrEqual(1);
    });
}

test("skip link is hidden until keyboard navigation reaches it", async ({ page }) => {
    await page.goto("/", { waitUntil: "networkidle" });
    const skipLink = page.getByRole("link", { name: "Skip to main content" });

    const hiddenBox = await skipLink.boundingBox();
    expect(hiddenBox?.y ?? 0).toBeLessThan(0);
    expect(await skipLink.evaluate(element => getComputedStyle(element).clipPath))
        .toContain("100%");

    // FocusOnNavigate deliberately starts at the new page heading. Walk back
    // through the compact landing navigation exactly as a keyboard user can.
    for (let attempt = 0; attempt < 10 && !(await skipLink.evaluate(element => element === document.activeElement)); attempt += 1) {
        await page.keyboard.press("Shift+Tab");
    }

    await expect(skipLink).toBeFocused();
    await expect.poll(async () => (await skipLink.boundingBox())?.y ?? -1).toBeGreaterThanOrEqual(0);
    expect(await skipLink.evaluate(element => getComputedStyle(element).clipPath))
        .not.toContain("100%");
});
