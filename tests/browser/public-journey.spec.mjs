import { expect, test } from "@playwright/test";

const publicPages = [
    { name: "home", path: "/", heading: /your voice.*becomes.*instrument/i },
    { name: "join", path: "/join", heading: /join the party/i },
    { name: "display", path: "/display", heading: /Quizizzo|display/i },
    { name: "player", path: "/play", heading: /no active player/i },
    { name: "login", path: "/Account/Login", heading: /log in/i },
    { name: "register", path: "/Account/Register", heading: /register|create/i },
    { name: "forgot-password", path: "/Account/ForgotPassword", heading: /forgot your password/i },
    { name: "forgot-password-confirmation", path: "/Account/ForgotPasswordConfirmation", heading: /forgot password confirmation/i },
    { name: "resend-confirmation", path: "/Account/ResendEmailConfirmation", heading: /resend email confirmation/i },
    { name: "invalid-reset", path: "/Account/InvalidPasswordReset", heading: /invalid password reset/i },
    { name: "locked-out", path: "/Account/Lockout", heading: /locked out/i },
    { name: "access-denied", path: "/Account/AccessDenied", heading: /access denied/i },
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

test("home tells the complete couch co-op story while scrolling", async ({ page }) => {
    await page.goto("/", { waitUntil: "networkidle" });
    for (const section of [".voice-section", ".pile-feature", ".animates-feature", ".how-section", ".final-cta"]) {
        const target = page.locator(section);
        await target.scrollIntoViewIfNeeded();
        await expect(target).toBeVisible();
        await expect.poll(async () => target.evaluate(element =>
            Number.parseFloat(getComputedStyle(element).opacity))).toBeGreaterThan(.9);
        expect(await page.evaluate(() => document.documentElement.scrollWidth - innerWidth))
            .toBeLessThanOrEqual(1);
    }
    await expect(page.getByRole("heading", { name: /you don't need to sing well/i })).toBeVisible();
    await expect(page.getByRole("heading", { name: /Pile-Up Panic/i })).toBeAttached();
    await expect(page.getByRole("heading", { name: /Ani Mates/i })).toBeAttached();
});

test("browser back restores the complete landing page", async ({ page }) => {
    await page.goto("/", { waitUntil: "networkidle" });
    await page.getByRole("link", { name: "Join game" }).click();
    await expect(page).toHaveURL(/\/join$/);
    await page.goBack({ waitUntil: "networkidle" });

    await expect(page.getByRole("heading", { name: /your voice becomes the instrument/i }))
        .toBeVisible();
    await expect(page.locator(".hero-copy")).toHaveCSS("opacity", "1");
    await expect(page.locator(".voice-section")).toBeAttached();
});
