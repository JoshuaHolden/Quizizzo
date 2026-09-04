import { expect, test } from "@playwright/test";
import path from "node:path";

test.skip(process.env.QUIZIZZO_ADMIN_AUDIT !== "1",
    "Set QUIZIZZO_ADMIN_AUDIT=1 with a matching configured administrator email.");

test("administrator can add, discover and remove a dynamically analysed MIDI tune", async ({ page }) => {
    test.setTimeout(120000);
    const email = "midi-admin@example.test";
    const password = "MidiAdmin!2026Aa9";
    const errors = [];
    page.on("pageerror", error => errors.push(error.message));
    page.on("console", message => { if (message.type() === "error") errors.push(message.text()); });

    await page.goto("/Account/Register");
    await page.getByLabel("Email").fill(email);
    await page.getByLabel("Password", { exact: true }).fill(password);
    await page.getByLabel("Confirm Password").fill(password);
    await page.getByRole("button", { name: "Create host account" }).click();
    const confirmation = page.getByRole("link", { name: /confirm your account/i });
    if (await confirmation.isVisible()) await confirmation.click();
    await page.goto("/Account/Login");
    await page.getByLabel("Email").fill(email);
    await page.getByLabel("Password").fill(password);
    await page.getByRole("button", { name: "Log in", exact: true }).click();

    await page.goto("/admin/voicechoon");
    await expect(page.getByRole("heading", { name: "VoiceChoon song library" })).toBeVisible();
    // Wait for the interactive server circuit to replace the prerendered input.
    await page.waitForTimeout(1000);
    const staleAudit = page.locator(".admin-song-card").filter({ has: page.getByText("gs", { exact: true }) });
    if (await staleAudit.getByRole("button", { name: "Remove" }).isVisible()) {
        await staleAudit.getByRole("button", { name: "Remove" }).click();
        await expect(staleAudit).toHaveCount(0);
    }
    await page.getByLabel("MIDI file").setInputFiles(path.resolve(
        "src/Quizizzo.Games.VoiceChoon/Assets/gs.mid"));
    await page.getByLabel("Song name").fill("Dynamic Greensleeves Audit");
    await page.getByRole("button", { name: "Analyse and add tune" }).click();
    await expect(page.getByRole("status")).toContainText("Added Dynamic Greensleeves Audit");
    const uploadedCard = page.locator(".admin-song-card").filter({ hasText: "Dynamic Greensleeves Audit" });
    await expect(uploadedCard).toContainText("2–4 players");

    await page.reload();
    await expect(page.locator(".admin-song-card").filter({ hasText: "Dynamic Greensleeves Audit" })).toBeVisible();
    await page.goto("/host");
    await expect(page).toHaveURL(/\/display$/);
    await page.getByRole("button", { name: "Host controls" }).click();
    await page.locator("article").filter({ hasText: "VoiceChoon" })
        .locator("details").filter({ hasText: "Difficulty" }).locator("summary").click();
    await expect(page.locator("#voicechoon-song option", { hasText: "Dynamic Greensleeves Audit" })).toHaveCount(1);

    await page.goto("/admin/voicechoon");
    await page.locator(".admin-song-card").filter({ hasText: "Dynamic Greensleeves Audit" })
        .getByRole("button", { name: "Remove" }).click();
    await expect(page.getByRole("status")).toContainText("Removed Dynamic Greensleeves Audit");
    await expect(page.locator(".admin-song-card").filter({ hasText: "Dynamic Greensleeves Audit" })).toHaveCount(0);
    expect(errors).toEqual([]);
});
