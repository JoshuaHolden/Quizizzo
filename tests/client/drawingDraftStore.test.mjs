import test from "node:test";
import assert from "node:assert/strict";
import { DrawingDraftStore } from "../../src/Quizizzo.Web/wwwroot/js/drawingCanvas.js";

function memoryStorage(entries = {}) {
    const values = new Map(Object.entries(entries));
    return {
        get length() { return values.size; },
        key(index) { return [...values.keys()][index] ?? null; },
        getItem(key) { return values.get(key) ?? null; },
        setItem(key, value) { values.set(key, value); },
        removeItem(key) { values.delete(key); }
    };
}

test("clearing a submitted draft prevents dispose-time recreation", () => {
    const storage = memoryStorage();
    const drafts = new DrawingDraftStore(storage, "current");
    drafts.save("before-submit");

    drafts.clear();
    drafts.save("dispose-save");

    assert.equal(storage.getItem("current"), null);
});

test("obsolete drafts are removed only for the same party and player", () => {
    const storage = memoryStorage({
        "quizizzo:drawing:v1:party:old-game:round:player": "old",
        "quizizzo:drawing:v1:party:old-game:round:player:submission-id": "old-id",
        "quizizzo:drawing:v1:party:new-game:round:other-player": "other",
        "quizizzo:drawing:v1:another-party:game:round:player": "another"
    });
    const currentKey = "quizizzo:drawing:v1:party:new-game:round:player";
    const drafts = new DrawingDraftStore(storage, currentKey);
    storage.setItem(`${currentKey}:submission-id`, "current-id");

    drafts.removeObsolete("quizizzo:drawing:v1:party:", ":player");

    assert.equal(storage.getItem("quizizzo:drawing:v1:party:old-game:round:player"), null);
    assert.equal(storage.getItem("quizizzo:drawing:v1:party:old-game:round:player:submission-id"), null);
    assert.equal(storage.getItem(`${currentKey}:submission-id`), "current-id");
    assert.equal(storage.getItem("quizizzo:drawing:v1:party:new-game:round:other-player"), "other");
    assert.equal(storage.getItem("quizizzo:drawing:v1:another-party:game:round:player"), "another");
});

test("submission IDs survive refresh and are cleared only after success", () => {
    const storage = memoryStorage();
    const first = new DrawingDraftStore(storage, "current");
    const submissionId = first.getOrCreateSubmissionId(() => "11111111-1111-4111-8111-111111111111");
    const refreshed = new DrawingDraftStore(storage, "current");

    assert.equal(
        refreshed.getOrCreateSubmissionId(() => "22222222-2222-4222-8222-222222222222"),
        submissionId);
    refreshed.clear();
    assert.equal(storage.getItem("current:submission-id"), null);
});
