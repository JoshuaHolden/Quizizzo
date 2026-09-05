import test from "node:test";
import assert from "node:assert/strict";

import { encodeWave, findAudibleBounds, microphonePermissionState, normalizeAndFade } from
    "../../src/Quizizzo.Web/wwwroot/js/voiceRecorder.js";

test("microphone permission state distinguishes a saved grant from a fresh prompt", async () => {
    const navigatorDescriptor = Object.getOwnPropertyDescriptor(globalThis, "navigator");
    const secureDescriptor = Object.getOwnPropertyDescriptor(globalThis, "isSecureContext");
    try {
        Object.defineProperty(globalThis, "isSecureContext", { configurable: true, value: true });
        Object.defineProperty(globalThis, "navigator", { configurable: true, value: {
            mediaDevices: { getUserMedia() {} },
            permissions: { query: async () => ({ state: "granted" }) }
        } });
        assert.equal(await microphonePermissionState(), "granted");
        globalThis.navigator.permissions.query = async () => ({ state: "prompt" });
        assert.equal(await microphonePermissionState(), "prompt");
    } finally {
        if (navigatorDescriptor) Object.defineProperty(globalThis, "navigator", navigatorDescriptor);
        else delete globalThis.navigator;
        if (secureDescriptor) Object.defineProperty(globalThis, "isSecureContext", secureDescriptor);
        else delete globalThis.isSecureContext;
    }
});
import { nearestLaneNote, songPositionSeconds, visibleNotes } from
    "../../src/Quizizzo.Web/wwwroot/js/rhythmController.js";

test("recording processing trims silence, normalizes peaks and emits PCM wave", () => {
    const input = new Float32Array([0, 0, 0.1, 0.5, -0.25, 0]);
    assert.deepEqual(findAudibleBounds(input), { start: 2, end: 5 });

    const [processed] = normalizeAndFade([input], 1000);
    assert.equal(processed.length, 3);
    assert.ok(Math.max(...processed.map(Math.abs)) <= 0.9);

    const wave = encodeWave([processed], 1000);
    assert.equal(wave.type, "audio/wav");
    assert.equal(wave.size, 44 + processed.length * 2);
});
test("rhythm helpers derive song time and choose only the nearest matching lane", () => {
    const notes = [
        { id: "a", lane: 0, startTimeSeconds: 1 },
        { id: "b", lane: 1, startTimeSeconds: 1.1 },
        { id: "c", lane: 0, startTimeSeconds: 3 }
    ];
    assert.equal(songPositionSeconds("2026-09-04T12:00:00Z", Date.parse("2026-09-04T12:00:01.1Z")), 1.1);
    assert.deepEqual(visibleNotes(notes, 1, 2).map(note => note.id), ["a", "b", "c"]);
    assert.equal(nearestLaneNote(notes, 0, 1.08)?.id, "a");
    assert.equal(nearestLaneNote(notes, 1, 2), null);
});
