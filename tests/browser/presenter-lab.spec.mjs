import { expect, test } from "@playwright/test";

test("Kenney presenter uses named spritesheet atlas frames", async ({ page }, testInfo) => {
    const errors = [];
    const failedRequests = [];
    page.on("console", message => {
        if (message.type() === "error") errors.push(message.text());
    });
    page.on("pageerror", error => errors.push(error.message));
    page.on("requestfailed", request => failedRequests.push(`${request.url()}: ${request.failure()?.errorText}`));

    await page.goto("/presenter-lab", { waitUntil: "networkidle" });
    await expect(page.locator("[data-presenter-canvas] canvas")).toBeVisible();
    await expect.poll(() => page.locator("[data-presenter-lab]").evaluate(root => Boolean(root.presenterScene)))
        .toBe(true);

    const atlasState = await page.locator("[data-presenter-lab]").evaluate(root => {
        const textures = root.presenterScene.textures;
        return {
            face: textures.get("face").has("mouth_happy.png"),
            hair: textures.get("hair").has("brown1Man5.png"),
            pants: textures.get("pants").has("pantsNavy_long.png"),
            shirts: textures.get("shirts").has("navyShirt1.png"),
            shoes: textures.get("shoes").has("brownShoe1.png"),
            skin: textures.get("skin").has("tint1_head.png")
        };
    });
    expect(atlasState).toEqual({ face: true, hair: true, pants: true, shirts: true, shoes: true, skin: true });

    const randomise = page.getByRole("button", { name: /Randomise presenter/i });
    for (let attempt = 0; attempt < 32; attempt += 1) await randomise.click();
    for (const action of ["Wave", "Talking", "Laugh", "Celebrate", "Think", "Fart"]) {
        await page.getByRole("button", { name: new RegExp(`^${action}`) }).click();
    }
    await page.getByRole("button", { name: /^Idle/ }).click();
    await page.screenshot({ path: testInfo.outputPath("spritesheet-presenter.png"), fullPage: true });

    expect(errors).toEqual([]);
    expect(failedRequests).toEqual([]);
});
