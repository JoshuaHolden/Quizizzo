import assert from "node:assert/strict";
import test from "node:test";

const audioInstances = [];

class FakeAudio {
    constructor(source = "") {
        this.src = source;
        this.currentTime = 0;
        this.duration = 20;
        this.playCount = 0;
        this.pauseCount = 0;
        this.loadCount = 0;
        this.paused = true;
        this.volume = 1;
        this.loop = false;
        this.listeners = new Map();
        audioInstances.push(this);
    }

    play() {
        this.playCount++;
        this.paused = false;
        return this.playError ? Promise.reject(this.playError) : Promise.resolve();
    }

    pause() {
        this.pauseCount++;
        this.paused = true;
    }

    addEventListener(name, callback) {
        const listeners = this.listeners.get(name) || [];
        listeners.push(callback);
        this.listeners.set(name, listeners);
    }

    emit(name) {
        for (const listener of this.listeners.get(name) || []) listener();
    }

    removeAttribute(name) {
        if (name === "src") this.src = "";
    }

    load() {
        this.loadCount++;
    }
}

const localStorageValues = new Map();
const sessionStorageValues = new Map();
const storage = values => ({
    getItem: key => values.get(key) ?? null,
    setItem: (key, value) => values.set(key, value)
});

globalThis.Audio = FakeAudio;
globalThis.document = {
    addEventListener() { },
    removeEventListener() { }
};
globalThis.window = {
    localStorage: storage(localStorageValues),
    sessionStorage: storage(sessionStorageValues),
    setTimeout,
    clearTimeout,
    setInterval,
    clearInterval
};

await import("../../src/Quizizzo.Web/wwwroot/js/presentationAudio.js");

const audio = window.quizizzoPresentationAudio;
const nextTurn = () => new Promise(resolve => setImmediate(resolve));
const futureDeadline = seconds => new Date(Date.now() + seconds * 1000).toISOString();
const slopSnapshot = (phase, overrides = {}) => ({
    mode: "Game",
    gameKey: "slop-machine",
    phase,
    phaseEndsAtUtc: null,
    revision: 1,
    roomCode: "TEST",
    gameInstanceId: "game-1",
    ...overrides
});
const createController = stateChanged => audio.create(stateChanged || (() => { }), {
    fadeInMilliseconds: 0,
    fadeOutMilliseconds: 0,
    crossfadeMilliseconds: 0,
    scoreboardToVictoryMilliseconds: 0
});

test.beforeEach(() => {
    audioInstances.length = 0;
    localStorageValues.clear();
    sessionStorageValues.clear();
});

test("every Slop Machine phase maps to its intended background track", () => {
    const expected = new Map([
        ["GameIntro", "lobby"], ["FreshSlopIntro", null],
        ["FreshSlopWriting", "slopWriting"], ["FreshSlopReveal", "slopVoting"],
        ["FreshSlopVoting", "slopVoting"], ["FreshSlopResults", null],
        ["AlgorithmRouletteIntro", null], ["AlgorithmRouletteSpinning", "slopSpinner"],
        ["AlgorithmRouletteWriting", "slopWriting"], ["AlgorithmRouletteReveal", "slopVoting"],
        ["AlgorithmRouletteVoting", "slopVoting"], ["AlgorithmRouletteResults", null],
        ["ThumbnailTelephoneIntro", "slopTelephone"], ["TelephoneWriting", "slopTelephone"],
        ["TelephoneMatching", "slopTelephone"], ["TelephoneReveal", "slopTelephone"],
        ["TelephoneVoting", "slopTelephone"], ["TelephoneResults", "slopTelephone"],
        ["CommentsIntro", "slopComments"], ["CommentsWriting", "slopComments"],
        ["CommentsReveal", "slopComments"], ["CommentsVoting", "slopComments"],
        ["CommentsResults", "slopComments"], ["ScoreReview1", "slopScoreboard"],
        ["ScoreReview2", "slopScoreboard"], ["ScoreReview3", "slopScoreboard"],
        ["ScoreReview4", "slopScoreboard"], ["FinalIntro", "slopFinal"],
        ["FinalWriting", "slopFinal"], ["FinalReveal", "slopFinal"],
        ["FinalVoting", "slopFinal"], ["FinalMachineGuess", "slopFinal"],
        ["FinalResults", "slopFinal"], ["FinalScoreReview", "slopScoreboard"],
        ["WinnerCelebration", null], ["Completed", null]
    ]);

    for (const [phase, trackKey] of expected) {
        const state = audio.backgroundState(slopSnapshot(phase), Date.now());
        assert.equal(state?.trackKey ?? null, trackKey, phase);
    }
    assert.equal(audio.backgroundState({ mode: "Lobby", phase: "Lobby" })?.trackKey, "lobby");
});

test("lobby music survives roster and revision updates without restarting", async () => {
    const controller = createController();
    controller.update({ mode: "Lobby", phase: "Lobby", revision: 1, players: [] });
    await nextTurn();
    const background = controller.activeBackground;
    controller.update({ mode: "Lobby", phase: "Lobby", revision: 2, players: [{ id: "p1" }] });
    await nextTurn();

    assert.equal(controller.activeTrackKey, "lobby");
    assert.equal(controller.activeBackground, background);
    assert.equal(background.playCount, 1);
    controller.destroy();
});

test("countdown starts once at the authoritative final twenty seconds and uses reconnect offset", async () => {
    const deadline = futureDeadline(10);
    const controller = createController();
    controller.update(slopSnapshot("FreshSlopWriting", { phaseEndsAtUtc: deadline }));
    await nextTurn();
    const countdown = controller.activeBackground;

    assert.equal(controller.activeTrackKey, "slopCountdown");
    assert.equal(countdown.playCount, 1);
    assert.ok(countdown.currentTime >= 9.5 && countdown.currentTime <= 10.5);
    controller.update(slopSnapshot("FreshSlopWriting", { phaseEndsAtUtc: deadline, revision: 2 }));
    await nextTurn();
    assert.equal(countdown.playCount, 1);
    controller.destroy();
});

test("early writing completion stops countdown and matching never starts it", async () => {
    const controller = createController();
    controller.update(slopSnapshot("TelephoneWriting", { phaseEndsAtUtc: futureDeadline(10) }));
    await nextTurn();
    const countdown = controller.activeBackground;
    assert.equal(controller.activeTrackKey, "slopCountdown");

    controller.update(slopSnapshot("TelephoneMatching", { phaseEndsAtUtc: futureDeadline(10), revision: 2 }));
    await nextTurn();
    assert.equal(controller.activeTrackKey, "slopTelephone");
    assert.equal(countdown.paused, true);
    controller.destroy();
});

test("reveal music continues into voting without restarting", async () => {
    const controller = createController();
    controller.update(slopSnapshot("FreshSlopReveal"));
    await nextTurn();
    const voting = controller.activeBackground;
    controller.update(slopSnapshot("FreshSlopVoting", { revision: 2 }));
    await nextTurn();
    assert.equal(controller.activeBackground, voting);
    assert.equal(voting.playCount, 1);
    controller.destroy();
});

test("score reviews and final phases transition through scoreboard, final and countdown music", async () => {
    const controller = createController();
    controller.update(slopSnapshot("ScoreReview3"));
    await nextTurn();
    assert.equal(controller.activeTrackKey, "slopScoreboard");
    controller.update(slopSnapshot("FinalIntro", { revision: 2 }));
    await nextTurn();
    assert.equal(controller.activeTrackKey, "slopFinal");
    controller.update(slopSnapshot("FinalWriting", { revision: 3, phaseEndsAtUtc: futureDeadline(10) }));
    await nextTurn();
    assert.equal(controller.activeTrackKey, "slopCountdown");
    controller.update(slopSnapshot("FinalReveal", { revision: 4 }));
    await nextTurn();
    assert.equal(controller.activeTrackKey, "slopFinal");
    controller.destroy();
});

test("machine and human victory cues are state driven, one-shot and reconnect safe", async () => {
    assert.equal(audio.cueState(slopSnapshot("FinalMachineGuess", {
        phaseMessage: "THE MACHINE WON. It has already posted an apology video."
    }))?.trackKey, "slopMachineVictory");
    assert.equal(audio.cueState(slopSnapshot("FinalMachineGuess", {
        phaseMessage: "Humanity wins the feed. Now spot both machine titles."
    })), null);

    const controller = createController();
    const celebration = slopSnapshot("WinnerCelebration", { revision: 42 });
    controller.update(celebration);
    await nextTurn();
    assert.equal(controller.activeTrackKey, "slopHumanVictory");
    assert.equal(controller.cue.playCount, 1);
    controller.update({ ...celebration, players: [{ id: "joint-winner" }] });
    await nextTurn();
    assert.equal(controller.cue.playCount, 1);
    controller.destroy();

    const reconnected = createController();
    reconnected.update(celebration);
    await nextTurn();
    assert.equal(reconnected.cue.playCount, 0);
    reconnected.destroy();

    const rematch = createController();
    rematch.update({ ...celebration, gameInstanceId: "game-2" });
    await nextTurn();
    assert.equal(rematch.cue.playCount, 1);
    rematch.destroy();
});

test("stale snapshots cannot restart an earlier soundtrack", async () => {
    const controller = createController();
    controller.update(slopSnapshot("FreshSlopVoting", { revision: 8 }));
    await nextTurn();
    controller.update(slopSnapshot("FreshSlopWriting", { revision: 7 }));
    await nextTurn();

    assert.equal(controller.activeTrackKey, "slopVoting");
    controller.destroy();
});

test("mute pauses playback, persists, and prevents audio until enabled", async () => {
    const controller = createController();
    controller.update({ mode: "Lobby", phase: "Lobby" });
    await nextTurn();
    await controller.toggle();
    assert.equal(controller.muted, true);
    assert.equal(localStorageValues.get("quizizzo.display.audio-muted"), "true");
    assert.ok(controller.backgrounds.every(track => track.paused));
    controller.destroy();

    const restored = createController();
    assert.equal(restored.muted, true);
    restored.update({ mode: "Lobby", phase: "Lobby" });
    await nextTurn();
    assert.equal(restored.backgrounds.reduce((sum, track) => sum + track.playCount, 0), 0);
    restored.destroy();
});

test("a missing track warns once, is marked unavailable and does not block state changes", async () => {
    const warnings = [];
    const originalWarn = console.warn;
    console.warn = message => warnings.push(message);
    try {
        const controller = createController();
        controller.update(slopSnapshot("AlgorithmRouletteSpinning"));
        await nextTurn();
        const spinner = controller.activeBackground;
        spinner.emit("error");
        await nextTurn();
        spinner.emit("error");
        controller.update(slopSnapshot("AlgorithmRouletteSpinning", { revision: 2 }));
        await nextTurn();
        assert.ok(controller.unavailable.has("slopSpinner"));
        assert.equal(warnings.length, 1);
        assert.equal(spinner.playCount, 1);

        controller.update(slopSnapshot("AlgorithmRouletteWriting", { revision: 3 }));
        await nextTurn();
        assert.equal(controller.activeTrackKey, "slopWriting");
        controller.destroy();
    } finally {
        console.warn = originalWarn;
    }
});

test("background transitions and one-shot cues never leave two long-form tracks playing", async () => {
    const controller = createController();
    controller.update(slopSnapshot("FreshSlopWriting"));
    await nextTurn();
    controller.update(slopSnapshot("FreshSlopReveal", { revision: 2 }));
    await nextTurn();
    assert.equal(controller.backgrounds.filter(track => !track.paused).length, 1);
    controller.update(slopSnapshot("WinnerCelebration", { revision: 3 }));
    await nextTurn();
    assert.equal(controller.backgrounds.filter(track => !track.paused).length, 0);
    assert.equal(controller.cue.paused, false);
    controller.destroy();
});

test("AniMates keeps its existing deadline soundtrack behavior", async () => {
    const controller = createController();
    controller.update({
        mode: "Game",
        gameKey: "animates",
        phase: "Drawing",
        phaseEndsAtUtc: futureDeadline(10)
    });
    await nextTurn();
    assert.equal(controller.activeTrackKey, "countdown");
    assert.ok(controller.activeBackground.currentTime >= 9.5);
    controller.destroy();
});
