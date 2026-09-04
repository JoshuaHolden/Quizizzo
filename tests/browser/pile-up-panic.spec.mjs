import { expect, test } from "@playwright/test";

test.skip(
    process.env.QUIZIZZO_FULL_PARTY_AUDIT !== "1",
    "Set QUIZIZZO_FULL_PARTY_AUDIT=1 to run live multiplayer journeys.");
test.use({ trace: "off" });

async function gotoReliable(page, url) {
    for (let attempt = 0; attempt < 3; attempt += 1) {
        try {
            return await page.goto(url, { waitUntil: "domcontentloaded", timeout: 30_000 });
        } catch (error) {
            if (!/ERR_(ABORTED|NETWORK_CHANGED)/.test(String(error)) || attempt === 2) throw error;
            await page.waitForTimeout(500);
        }
    }
}

async function assertControllerFits(page) {
    const dimensions = await page.evaluate(() => ({
        width: window.innerWidth,
        scrollWidth: document.documentElement.scrollWidth,
        height: document.documentElement.clientHeight,
        scrollHeight: document.documentElement.scrollHeight,
    }));
    expect(dimensions.scrollWidth).toBeLessThanOrEqual(dimensions.width + 1);
    expect(dimensions.scrollHeight).toBeLessThanOrEqual(dimensions.height + 1);
}

async function createParty(host, playerCount) {
    const suffix = `${playerCount}-${Date.now()}-${Math.floor(Math.random() * 100_000)}`;
    const email = `pile-audit-${suffix}@example.test`;
    const password = `Pile!${suffix}Aa9`;
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
    await expect(host).toHaveURL(/\/display$/, { timeout: 20_000 });
    await expect(host.locator(".phaser-presentation canvas")).toBeVisible({ timeout: 20_000 });
    const roomCode = await host.locator(".phaser-presentation").getAttribute("data-room-code");
    expect(roomCode).toMatch(/^[A-HJ-KM-NP-Z2-9]{4}$/);
    return roomCode;
}

for (const playerCount of [2, 3, 4]) {
    test(`${playerCount}-player Pile-Up survives held input and display/controller refresh`, async ({ browser }, testInfo) => {
        test.setTimeout(180_000);
        const hostContext = await browser.newContext({ viewport: { width: 1440, height: 900 } });
        const playerContexts = [];
        try {
            const host = await hostContext.newPage();
            const roomCode = await createParty(host, playerCount);
            const players = [];
            for (let index = 0; index < playerCount; index += 1) {
                const landscape = index === playerCount - 1;
                const context = await browser.newContext({
                    viewport: landscape ? { width: 667, height: 375 } : { width: 320, height: 568 },
                    hasTouch: true,
                    isMobile: true,
                });
                playerContexts.push(context);
                const page = await context.newPage();
                page.on("console", message => {
                    if (message.type() === "error") {
                        process.stderr.write(`[player ${index + 1}] ${message.text()}\n`);
                    }
                });
                page.on("pageerror", error => {
                    process.stderr.write(`[player ${index + 1}] ${error.message}\n`);
                });
                await gotoReliable(page, `/join/${roomCode}`);
                await page.getByLabel("Player name").fill(`Pile ${index + 1}`);
                await page.getByRole("button", { name: "Join the party" }).click();
                await expect(page).toHaveURL(/\/play$/);
                await assertControllerFits(page);
                players.push(page);
            }

            await host.getByRole("button", { name: "Host controls" }).click();
            const card = host.locator(".display-host-game-card").filter({ hasText: "Pile-Up Panic" });
            await expect(card).toBeVisible();
            await expect(card.getByText("Realtime arcade · 2–4 players")).toBeVisible();
            await card.getByRole("button", { name: /Play now/ }).click();

            for (const player of players) {
                const readyChoice = player.locator(".vote-option input").first();
                await expect(readyChoice).toBeVisible({ timeout: 20_000 });
                await readyChoice.check();
                await player.getByRole("button", { name: "Ready up" }).click();
            }
            for (const player of players) {
                await expect(player.locator(".arcade-controller")).toBeVisible({ timeout: 20_000 });
                await assertControllerFits(player);
            }
            const presentation = host.locator(".phaser-presentation");
            await expect(presentation).toHaveAttribute("data-game-key", "pile-up-panic", { timeout: 20_000 });
            await expect(presentation).toHaveAttribute("data-phase", "Playing", { timeout: 20_000 });

            const heldControl = players[0].getByRole("button", { name: "Move left" });
            await heldControl.dispatchEvent("pointerdown", { pointerId: 1, pointerType: "touch" });
            await players[0].waitForTimeout(420);
            await heldControl.dispatchEvent("pointerup", { pointerId: 1, pointerType: "touch" });
            await players[0].getByRole("button", { name: "Rotate clockwise" }).click();

            await players[0].reload({ waitUntil: "domcontentloaded" });
            await expect(players[0].locator(".arcade-controller")).toBeVisible({ timeout: 20_000 });
            await assertControllerFits(players[0]);
            await host.reload({ waitUntil: "domcontentloaded" });
            await expect(host.locator(".phaser-presentation canvas")).toBeVisible({ timeout: 20_000 });
            await expect(host.locator(".phaser-presentation"))
                .toHaveAttribute("data-game-key", "pile-up-panic", { timeout: 20_000 });
            await expect(host.locator(".phaser-presentation"))
                .toHaveAttribute("data-phase", "Playing", { timeout: 20_000 });
            await expect(host.getByRole("button", { name: "Host controls" })).toBeVisible();
            await host.waitForTimeout(500);

            await host.screenshot({
                path: testInfo.outputPath(`pile-up-${playerCount}-display.png`),
                animations: "disabled",
            });
            await players[0].screenshot({
                path: testInfo.outputPath(`pile-up-${playerCount}-controller.png`),
                animations: "disabled",
            });
        } finally {
            await Promise.all(playerContexts.map(context => context.close()));
            await hostContext.close();
        }
    });
}
