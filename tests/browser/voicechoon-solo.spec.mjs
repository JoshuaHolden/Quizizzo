import { expect, test } from "@playwright/test";

test.skip(process.env.QUIZIZZO_VOICECHOON_AUDIT !== "1",
    "Set QUIZIZZO_VOICECHOON_AUDIT=1 to create a local solo VoiceChoon party.");
test.use({ trace: "off" });

function fakeMicrophone() {
    const wave = (() => {
        const rate = 16000;
        const frames = rate;
        const bytes = new ArrayBuffer(44 + frames * 2);
        const view = new DataView(bytes);
        const text = (offset, value) => [...value].forEach((letter, index) =>
            view.setUint8(offset + index, letter.charCodeAt(0)));
        text(0, "RIFF"); view.setUint32(4, bytes.byteLength - 8, true);
        text(8, "WAVE"); text(12, "fmt "); view.setUint32(16, 16, true);
        view.setUint16(20, 1, true); view.setUint16(22, 1, true);
        view.setUint32(24, rate, true); view.setUint32(28, rate * 2, true);
        view.setUint16(32, 2, true); view.setUint16(34, 16, true);
        text(36, "data"); view.setUint32(40, frames * 2, true);
        for (let index = 0; index < frames; index += 1) {
            const envelope = Math.min(1, index / 240, (frames - index - 1) / 240);
            const sample = Math.sin(2 * Math.PI * 220 * index / rate) * 0.35 * envelope;
            view.setInt16(44 + index * 2, sample * 0x7fff, true);
        }
        return bytes;
    })();
    navigator.mediaDevices ??= {};
    navigator.mediaDevices.getUserMedia = async () => ({
        getTracks: () => [{ readyState: "live", stop() { this.readyState = "ended"; } }]
    });
    globalThis.MediaRecorder = class extends EventTarget {
        constructor() { super(); this.state = "inactive"; this.mimeType = "audio/wav"; }
        start() { this.state = "recording"; }
        stop() {
            if (this.state !== "recording") return;
            this.state = "inactive";
            const event = new Event("dataavailable");
            Object.defineProperty(event, "data", { value: new Blob([wave], { type: "audio/wav" }) });
            this.dispatchEvent(event);
            this.dispatchEvent(new Event("stop"));
        }
    };
    globalThis.__voiceStarts = [];
    const AudioContextType = globalThis.AudioContext || globalThis.webkitAudioContext;
    if (AudioContextType) {
        const original = AudioBufferSourceNode.prototype.start;
        AudioBufferSourceNode.prototype.start = function (...args) {
            globalThis.__voiceStarts.push({ at: performance.now(), when: args[0] ?? 0 });
            return original.apply(this, args);
        };
    }
}

async function registerHost(page) {
    const suffix = `${Date.now()}-${Math.floor(Math.random() * 100000)}`;
    const email = `voicechoon-${suffix}@example.test`;
    const password = `Audit!${suffix}Aa9`;
    await page.goto("/Account/Register");
    await page.getByLabel("Email").fill(email);
    await page.getByLabel("Password", { exact: true }).fill(password);
    await page.getByLabel("Confirm Password").fill(password);
    await page.getByRole("button", { name: "Create host account" }).click();
    await page.getByRole("link", { name: /confirm your account/i }).click();
    await page.goto("/Account/Login");
    await page.getByLabel("Email").fill(email);
    await page.getByLabel("Password").fill(password);
    await page.getByRole("button", { name: "Log in", exact: true }).click();
}

test("injected WAV sounds play Greensleeves in solo autoplay without refresh bursts", async ({ browser }, testInfo) => {
    test.setTimeout(240000);
    const hostContext = await browser.newContext({ viewport: { width: 1440, height: 900 } });
    const playerContext = await browser.newContext({ viewport: { width: 667, height: 375 }, hasTouch: true });
    await hostContext.addInitScript(fakeMicrophone);
    await playerContext.addInitScript(fakeMicrophone);
    const failures = [];
    for (const context of [hostContext, playerContext]) {
        context.on("page", page => {
            page.on("pageerror", error => {
                if (!error.message.includes("Resident credentials or empty 'allowCredentials'"))
                    failures.push(error.message);
            });
            page.on("console", message => {
                if (message.type() === "error" &&
                    !message.text().includes("Resident credentials or empty 'allowCredentials'"))
                    failures.push(message.text());
            });
            page.on("response", response => {
                if (response.url().includes("/api/voicechoon/") && response.status() >= 400)
                    failures.push(`${response.status()} ${response.url()}`);
            });
        });
    }
    const host = await hostContext.newPage();
    await registerHost(host);
    await host.goto("/host");
    await expect(host).toHaveURL(/\/display$/);
    // Let the interactive server circuit replace the prerendered display controls.
    await host.waitForTimeout(1000);
    await host.getByRole("button", { name: "Host controls" }).click();
    const roomLabel = await host.getByText(/^Room [A-HJ-KM-NP-Z2-9]{4}$/).innerText();
    const roomCode = roomLabel.match(/[A-HJ-KM-NP-Z2-9]{4}/)?.[0];
    expect(roomCode).toMatch(/^[A-HJ-KM-NP-Z2-9]{4}$/);

    const player = await playerContext.newPage();
    const joinResponse = await player.goto(`http://localhost:8081/join/${roomCode}`);
    expect(joinResponse?.status()).toBe(200);
    await player.getByLabel("Player name").fill("Wavbot");
    await player.getByRole("button", { name: "Join the party" }).click();
    await expect(player).toHaveURL(/\/play$/);

    const card = host.locator("article").filter({ hasText: "VoiceChoon" });
    await card.locator("details").filter({ hasText: "Difficulty" }).locator("summary").click();
    await card.locator("#voicechoon-song").selectOption("greensleeves");
    await card.getByLabel("Solo autoplay test").check();
    await card.getByRole("button", { name: /Play now/ }).click();
    await expect(host.getByText("gs.mid", { exact: true })).toBeVisible();
    await host.getByRole("button", { name: "Continue now" }).click();

    await expect(player.locator(".voice-recording-card").first()).toBeVisible({ timeout: 20000 });
    await host.getByRole("button", { name: "Close host controls" }).click();
    const cards = player.locator(".voice-recording-card");
    const count = await cards.count();
    expect(count).toBeGreaterThan(0);
    for (let index = 0; index < count; index += 1) {
        const current = cards.nth(index);
        await current.getByRole("button", { name: /^Record / }).click();
        await current.getByRole("button", { name: /Stop/ }).click();
        await expect(current.getByRole("button", { name: "Use sound" })).toBeVisible();
        await current.getByRole("button", { name: "Use sound" }).click();
        await expect(current).toHaveClass(/accepted/);
    }
    await player.getByRole("button", { name: "Lock in my sounds" }).click();
    await expect(player.getByRole("button", { name: "My four pads are ready" })).toBeVisible();
    await player.getByRole("button", { name: "My four pads are ready" }).click();
    await expect(player.getByText("AUTO-PERFORMING")).toBeVisible({ timeout: 20000 });

    await player.waitForTimeout(8000);
    await host.screenshot({ path: testInfo.outputPath("voicechoon-music-video-stage.png"), fullPage: true });
    const beforeRefresh = await host.evaluate(() => globalThis.__voiceStarts.length);
    expect(beforeRefresh).toBeGreaterThan(0);
    await host.reload();
    await host.getByRole("button", { name: "Host controls" }).click();
    await expect(player.getByText("AUTO-PERFORMING")).toBeVisible({ timeout: 20000 });
    await host.waitForTimeout(1500);
    const resumedStarts = await host.evaluate(() => globalThis.__voiceStarts);
    // Greensleeves contains real chord clusters, so several simultaneous voices are expected.
    // The old fault restarted hundreds of elapsed notes with source.start(0).
    expect(resumedStarts.length).toBeLessThan(50);
    expect(resumedStarts.every(item => item.when > 0)).toBe(true);
    const largestBurst = Math.max(0, ...resumedStarts.map(item =>
        resumedStarts.filter(other => Math.abs(other.at - item.at) < 20).length));
    expect(largestBurst).toBeLessThanOrEqual(10);

    const sharePanel = host.locator(".voicechoon-share-panel");
    await expect(sharePanel).toBeVisible({ timeout: 150000 });
    await expect(sharePanel.getByText("Your music video is ready")).toBeVisible();
    await expect(sharePanel.getByRole("button", { name: /Share video|Download video/ }))
        .toBeVisible({ timeout: 15000 });
    const replayLink = sharePanel.getByRole("link", { name: "Watch permanent replay" });
    const replayUrl = await replayLink.getAttribute("href");
    expect(replayUrl).toMatch(/\/replay\/voicechoon\/[A-Za-z0-9_-]{16,64}$/);
    const replayPage = await hostContext.newPage();
    const replayResponse = await replayPage.goto(replayUrl);
    expect(replayResponse?.status()).toBe(200);
    await expect(replayPage.getByText("VoiceChoon replay", { exact: true })).toBeVisible();
    await expect(replayPage.locator("canvas")).toBeVisible({ timeout: 10000 });
    await expect.poll(async () => replayPage.evaluate(() => globalThis.__voiceStarts.length),
        { timeout: 15000 }).toBeGreaterThan(0);
    expect(failures).toEqual([]);
});
