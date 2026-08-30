import { expect, test } from "@playwright/test";

test.skip(
    process.env.QUIZIZZO_FULL_PARTY_AUDIT !== "1",
    "Set QUIZIZZO_FULL_PARTY_AUDIT=1 to create local audit users and parties.");
test.use({ trace: "off" });

function watchBrowser(context, role) {
    const errors = [];
    context.on("page", page => {
        page.on("console", message => {
            if (message.type() === "error") {
                errors.push(`${role} console: ${message.text()}`);
            }
        });
        page.on("pageerror", error => errors.push(`${role} page: ${error.message}`));
        page.on("requestfailed", request => {
            const failure = request.failure()?.errorText ?? "failed";
            if (!request.isNavigationRequest() ||
                (failure !== "net::ERR_ABORTED" && failure !== "net::ERR_NETWORK_CHANGED")) {
                errors.push(`${role} request: ${request.method()} ${request.url()}: ${failure}`);
            }
        });
    });
    return errors;
}

async function assertNoHorizontalOverflow(page) {
    const overflow = await page.evaluate(
        () => document.documentElement.scrollWidth - window.innerWidth);
    expect(overflow, `horizontal overflow at ${page.url()}`).toBeLessThanOrEqual(1);
}

async function gotoReliable(page, url) {
    for (let attempt = 0; attempt < 3; attempt += 1) {
        try {
            return await page.goto(url, { waitUntil: "domcontentloaded", timeout: 30_000 });
        } catch (error) {
            const transient = /ERR_(ABORTED|NETWORK_CHANGED)/.test(String(error));
            if (!transient || attempt === 2) {
                throw error;
            }
            await page.waitForTimeout(500);
        }
    }
    throw new Error(`Navigation to ${url} did not complete.`);
}

test("host, display, and two players can reach a live Estimate controller", async ({
    browser,
}, testInfo) => {
    test.setTimeout(240_000);

    const contexts = {
        host: await browser.newContext({ viewport: { width: 1280, height: 800 } }),
        display: await browser.newContext({ viewport: { width: 1440, height: 900 } }),
        playerOne: await browser.newContext({
            viewport: { width: 320, height: 568 },
            hasTouch: true,
            isMobile: true,
        }),
        playerTwo: await browser.newContext({
            viewport: { width: 390, height: 844 },
            hasTouch: true,
            isMobile: true,
        }),
    };
    const errorLogs = Object.entries(contexts)
        .map(([role, context]) => watchBrowser(context, role));

    const host = await contexts.host.newPage();
        const suffix = `${Date.now()}-${Math.floor(Math.random() * 100_000)}`;
        const email = `browser-audit-${suffix}@example.test`;
        const password = `Audit!${suffix}Aa9`;

        await gotoReliable(host, "/Account/Register");
        await host.getByLabel("Email").fill(email);
        await host.getByLabel("Password", { exact: true }).fill(password);
        await host.getByLabel("Confirm Password").fill(password);
        await host.getByRole("button", { name: "Create host account", exact: true }).click();
        await host.getByRole("link", { name: /confirm your account/i }).click();
        await gotoReliable(host, "/Account/Login");
        await host.getByLabel("Email").fill(email);
        await host.getByLabel("Password").fill(password);
        await host.getByRole("button", { name: "Log in", exact: true }).click();

        await gotoReliable(host, "/host");
        await expect(host.getByRole("heading", { name: "Host your party" })).toBeVisible();
        const createParty = host.getByRole("button", { name: "Start new party" });
        await expect(createParty).toBeEnabled();
        await host.waitForTimeout(1_500);
        await createParty.click();
        await expect(host).toHaveURL(/\/host\/party\/[0-9a-f-]+/i, { timeout: 15_000 });
        const partyUrl = host.url();
        const roomCode = (await host.locator("header h1").innerText()).trim();
        expect(roomCode).toMatch(/^[A-HJ-KM-NP-Z2-9]{4}$/);

        const display = await contexts.display.newPage();
        await gotoReliable(display, "/display");
        await expect(display.getByRole("heading", { name: "Pair this screen" })).toBeVisible();
        const pairingHref = await display
            .locator('a[href*="host/pair-display/"]')
            .getAttribute("href");
        expect(pairingHref).toBeTruthy();

        await gotoReliable(host, pairingHref);
        const pairDisplay = host.getByRole("button", { name: "Pair display" });
        await expect(pairDisplay).toBeEnabled();
        await host.waitForTimeout(1_500);
        await pairDisplay.click();
        await expect(host.getByText(`Display paired with room ${roomCode}.`))
            .toBeVisible({ timeout: 15_000 });
        await expect(display.getByRole("heading", { name: "JOIN THE PARTY" }))
            .toBeVisible({ timeout: 20_000 });
        const displayCanvas = display.locator(".phaser-presentation canvas");
        await expect(displayCanvas).toBeVisible();
        const initialCanvasWidth = await displayCanvas.evaluate(canvas => canvas.width);
        await display.setViewportSize({ width: 1920, height: 1080 });
        await expect.poll(() => displayCanvas.evaluate(canvas => canvas.width))
            .toBeGreaterThan(initialCanvasWidth);
        await expect.poll(() => displayCanvas.evaluate(canvas =>
            Math.round(canvas.getBoundingClientRect().width)))
            .toBe(1920);

        const playerOne = await contexts.playerOne.newPage();
        const playerTwo = await contexts.playerTwo.newPage();
        for (const [page, name] of [[playerOne, "Pixel"], [playerTwo, "Nova"]]) {
            await gotoReliable(page, `/join/${roomCode}`);
            await expect(page.locator("[data-avatar-preview] canvas")).toBeVisible();
            await page.getByLabel("Player name").fill(name);
            await page.getByLabel("Style").selectOption(name === "Pixel" ? "Woman" : "Man");
            await page.getByLabel("Skin").selectOption(name === "Pixel" ? "Tint7" : "Tint3");
            await page.getByLabel("Hair").selectOption(name === "Pixel" ? "Red" : "Black");
            await page.getByLabel("Length").selectOption(name === "Pixel" ? "Shorts" : "Cropped");
            await page.screenshot({
                path: testInfo.outputPath(`${name.toLowerCase()}-avatar-designer.png`),
                fullPage: true,
                animations: "disabled",
            });
            await page.getByRole("button", { name: "Join the party" }).click();
            await expect(page).toHaveURL(/\/play$/);
            await expect(page.getByRole("heading", { name: new RegExp(`You're in, ${name}`, "i") }))
                .toBeVisible();
            await page.getByRole("button", { name: name === "Pixel" ? "Send a kiss" : "Show anger" }).click();
        }

        await gotoReliable(host, partyUrl);
        const startEstimate = host.getByRole("button", { name: "Start Estimate" });
        await expect(startEstimate).toBeEnabled({ timeout: 20_000 });
        await host.screenshot({
            path: testInfo.outputPath("host-lobby.png"),
            fullPage: true,
            animations: "disabled",
        });
        await display.screenshot({
            path: testInfo.outputPath("display-lobby.png"),
            fullPage: true,
            animations: "disabled",
        });
        await host.waitForTimeout(1_000);
        await startEstimate.click();

        for (const [page, value] of [[playerOne, "50"], [playerTwo, "60"]]) {
            const submit = page.getByRole("button", { name: "Lock in my guess" });
            await expect(submit).toBeVisible({ timeout: 20_000 });
            const numberInput = page.getByLabel("Your estimate");
            const minimum = Number(await numberInput.getAttribute("min"));
            const maximum = Number(await numberInput.getAttribute("max"));
            const requested = Number(value);
            const safeValue = String(Math.min(maximum, Math.max(minimum, requested)));
            await numberInput.fill(safeValue);
            await expect(submit).toBeEnabled();
            await submit.click();
            await expect(submit).toBeHidden({ timeout: 20_000 });
            await expect(page.getByRole("heading", { name: /locked|round results/i }))
                .toBeVisible({ timeout: 20_000 });
            await expect(page.getByRole("link", { name: "Skip to main content" }))
                .not.toBeFocused();
        }

        await expect(host.getByText("Results", { exact: true }))
            .toBeVisible({ timeout: 20_000 });

        await playerOne.screenshot({
            path: testInfo.outputPath("player-waiting.png"),
            fullPage: true,
            animations: "disabled",
        });
        await host.screenshot({
            path: testInfo.outputPath("host-estimate.png"),
            fullPage: true,
            animations: "disabled",
        });

        for (const page of [host, display, playerOne, playerTwo]) {
            await assertNoHorizontalOverflow(page);
        }
    expect(errorLogs.flat(), "browser errors across role contexts").toEqual([]);
});
