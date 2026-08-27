import test from "node:test";
import assert from "node:assert/strict";
import {
    DrawingDocument,
    DrawingTool
} from "../../src/Quizizzo.Web/wwwroot/js/drawingDocument.mjs";

const baseOptions = {
    logicalWidth: 512,
    logicalHeight: 512,
    frameCount: 3,
    colours: ["#111111", "#ef4444", "#ffffff"],
    widths: [2, 5, 9, 16, 28],
    identity: {
        partyId: "party",
        gameSessionId: "game",
        roundId: "round-1",
        playerId: "player"
    },
    onionSkinEnabled: true
};

test("single-frame documents are first-class and safely bounded", () => {
    const drawing = new DrawingDocument({ ...baseOptions, frameCount: 1 });

    assert.equal(drawing.frameCount, 1);
    assert.equal(drawing.previousFrame, null);
    assert.equal(drawing.onionSkinEnabled, false);
    assert.equal(drawing.setFrame(-1), false);
    assert.equal(drawing.setFrame(1), false);
    assert.equal(drawing.currentFrameIndex, 0);
    drawing.setOnionSkin(true);
    assert.equal(drawing.onionSkinEnabled, false);
});

test("undo and clear affect only the current frame", () => {
    const drawing = new DrawingDocument(baseOptions);
    drawing.beginStroke({ x: 10, y: 10 });
    drawing.addPoint({ x: 20, y: 20 });
    drawing.endStroke();
    drawing.setFrame(1);
    drawing.beginStroke({ x: 30, y: 30 });
    drawing.endStroke();

    assert.equal(drawing.undo(), true);
    assert.equal(drawing.frames[1].strokes.length, 0);
    assert.equal(drawing.frames[0].strokes.length, 1);
    assert.equal(drawing.clearFrame(), false);
    drawing.setFrame(0);
    assert.equal(drawing.clearFrame(), true);
    assert.equal(drawing.frames[0].strokes.length, 0);
});

test("eraser behaves as a stroke tool without losing pen colour or size", () => {
    const drawing = new DrawingDocument(baseOptions);
    drawing.setColour("#ef4444");
    drawing.setWidth(16);
    drawing.setTool(DrawingTool.Eraser);
    drawing.beginStroke({ x: 50, y: 50 });
    drawing.endStroke();
    drawing.setTool(DrawingTool.Pen);

    assert.equal(drawing.selectedColour, "#ef4444");
    assert.equal(drawing.selectedWidth, 16);
    assert.equal(drawing.frames[0].strokes[0].tool, DrawingTool.Eraser);
});

test("draft serialization restores frames, tools, and identity", () => {
    const drawing = new DrawingDocument(baseOptions);
    drawing.setFrame(2);
    drawing.setColour("#ef4444");
    drawing.setWidth(9);
    drawing.beginStroke({ x: -5, y: 600, pressure: 2 });
    drawing.endStroke();

    const restored = DrawingDocument.tryRestore(drawing.serialize(), baseOptions);

    assert.ok(restored);
    assert.equal(restored.currentFrameIndex, 2);
    assert.equal(restored.selectedColour, "#ef4444");
    assert.equal(restored.selectedWidth, 9);
    assert.deepEqual(restored.frames[2].strokes[0].points[0], {
        x: 0,
        y: 512,
        pressure: 1
    });
    assert.deepEqual(restored.identity, baseOptions.identity);
});

test("drafts from another round or malformed local data are rejected", () => {
    const drawing = new DrawingDocument(baseOptions);
    const serialized = drawing.serialize();
    const anotherRound = {
        ...baseOptions,
        identity: { ...baseOptions.identity, roundId: "round-2" }
    };

    assert.equal(DrawingDocument.tryRestore(serialized, anotherRound), null);
    assert.equal(DrawingDocument.tryRestore("not-json", baseOptions), null);
    assert.equal(DrawingDocument.tryRestore({ schemaVersion: 999 }, baseOptions), null);
});

test("the live document enforces its total point budget", () => {
    const drawing = new DrawingDocument(baseOptions);
    drawing.beginStroke({ x: 1, y: 1 });
    drawing.addPoint({ x: 2, y: 2 });
    assert.equal(drawing.totalPoints, 2);
    drawing.endStroke();
    drawing.totalPoints = 100000;

    assert.equal(drawing.beginStroke({ x: 3, y: 3 }), false);
    drawing.totalPoints = 2;
    assert.equal(drawing.undo(), true);
    assert.equal(drawing.totalPoints, 0);
});
