import assert from "node:assert/strict";
import test from "node:test";

class FakeAudio {
    constructor(source = "") {
        this.src = source;
        this.currentTime = 0;
        this.duration = 20;
        this.playCount = 0;
        this.pauseCount = 0;
    }

    play() {
        this.playCount++;
        return Promise.resolve();
    }

    pause() {
        this.pauseCount++;
    }

    removeAttribute(name) {
        if (name === "src") this.src = "";
    }

    load() { }
}

const storage = new Map();
globalThis.Audio = FakeAudio;
globalThis.document = {
    addEventListener() { },
    removeEventListener() { }
};
globalThis.window = {
    localStorage: {
        getItem: key => storage.get(key) ?? null,
        setItem: (key, value) => storage.set(key, value)
    },
    setTimeout,
    clearTimeout
};

await import("../../src/Quizizzo.Web/wwwroot/js/presentationAudio.js");

const nextTurn = () => new Promise(resolve => setImmediate(resolve));

test("lobby and game snapshots select their respective looping soundtracks", async () => {
    const controller = window.quizizzoPresentationAudio.create(() => { });

    controller.update({ mode: "Lobby", phase: "Lobby" });
    await nextTurn();
    assert.equal(controller.backgroundSource, window.quizizzoPresentationAudio.assets.lobby);
    assert.equal(controller.background.playCount, 1);

    controller.update({ mode: "Game", gameKey: "animates", phase: "Briefing" });
    await nextTurn();
    assert.equal(controller.backgroundSource, window.quizizzoPresentationAudio.assets.game);
    assert.equal(controller.background.playCount, 2);
    controller.destroy();
});

test("AniMates drawing switches to the countdown at twenty seconds remaining", async () => {
    const controller = window.quizizzoPresentationAudio.create(() => { });
    controller.update({
        mode: "Game",
        gameKey: "animates",
        phase: "Drawing",
        phaseEndsAtUtc: new Date(Date.now() + 10_000).toISOString()
    });
    await nextTurn();

    assert.equal(controller.countdown.playCount, 1);
    assert.ok(controller.countdown.currentTime >= 9.8);
    assert.equal(controller.background.playCount, 0);
    controller.destroy();
});

test("mute preference pauses playback and is restored on the next controller", async () => {
    const controller = window.quizizzoPresentationAudio.create(() => { });
    controller.update({ mode: "Lobby", phase: "Lobby" });
    await nextTurn();
    await controller.toggle();

    assert.equal(controller.muted, true);
    assert.equal(storage.get("quizizzo.display.audio-muted"), "true");
    controller.destroy();

    const restored = window.quizizzoPresentationAudio.create(() => { });
    assert.equal(restored.muted, true);
    restored.destroy();
    storage.clear();
});
