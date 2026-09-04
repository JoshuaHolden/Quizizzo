import test from "node:test";
import assert from "node:assert/strict";

import { encodeWave, findAudibleBounds, normalizeAndFade } from
    "../../src/Quizizzo.Web/wwwroot/js/voiceRecorder.js";
import { autoplayVisualNotes, dueAutoplayNotes, nearestLaneNote, songPositionSeconds, visibleNotes } from
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

test("solo autoplay schedules each newly due note exactly once", () => {
    const notes = [
        { id: "past", startTimeSeconds: 0.8 },
        { id: "due-a", startTimeSeconds: 1.1 },
        { id: "due-b", startTimeSeconds: 1.2 },
        { id: "future", startTimeSeconds: 1.3 }
    ];

    assert.deepEqual(
        dueAutoplayNotes(notes, 1, 1.2, new Set(["due-b"])).map(note => note.id),
        ["due-a"]);
});

test("solo autoplay represents rapid audible runs as aligned holds", () => {
    const notes = [
        { id: "a", lane: 0, startTimeSeconds: 1, durationSeconds: 0.1, type: "Tap" },
        { id: "b", lane: 0, startTimeSeconds: 1.2, durationSeconds: 0.1, type: "Tap" },
        { id: "c", lane: 0, startTimeSeconds: 1.4, durationSeconds: 0.2, type: "Tap" },
        { id: "d", lane: 1, startTimeSeconds: 1.1, durationSeconds: 0.1, type: "Tap" }
    ];

    const visual = autoplayVisualNotes(notes);

    assert.equal(visual.length, 2);
    assert.equal(visual[0].id, "visual-run-0-a");
    assert.equal(visual[0].lane, 0);
    assert.equal(visual[0].startTimeSeconds, 1);
    assert.equal(visual[0].type, "Hold");
    assert.ok(Math.abs(visual[0].durationSeconds - 0.6) < 0.0001);
    assert.equal(visual[1], notes[3]);
});
