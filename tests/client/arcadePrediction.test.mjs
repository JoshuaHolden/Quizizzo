import assert from "node:assert/strict";
import test from "node:test";
import { predictArcadeArena } from "../../src/Quizizzo.Web/wwwroot/js/arcadeController.js";

function arena(overrides = {}) {
    return {
        columns: 9,
        visibleRows: 17,
        hiddenRows: 3,
        settledCells: [],
        activePiece: { pieceKey: "corner", material: "aqua", x: 3, y: 3, rotation: 0 },
        upcomingPieces: [],
        pieceShapes: { corner: [{ x: 0, y: 0 }, { x: 0, y: 1 }, { x: 1, y: 1 }] },
        ...overrides
    };
}

test("predicts movement without mutating the authoritative arena", () => {
    const authoritative = arena();
    const predicted = predictArcadeArena(authoritative, ["MoveLeft", "SoftDrop"]);

    assert.equal(predicted.activePiece.x, 2);
    assert.equal(predicted.activePiece.y, 4);
    assert.equal(authoritative.activePiece.x, 3);
    assert.equal(authoritative.activePiece.y, 3);
});

test("rejects predicted movement through walls and settled cells", () => {
    const againstWall = predictArcadeArena(arena({
        activePiece: { pieceKey: "corner", material: "aqua", x: 0, y: 3, rotation: 0 }
    }), ["MoveLeft"]);
    const againstCell = predictArcadeArena(arena({
        settledCells: [{ x: 2, y: 4, material: "junk" }]
    }), ["MoveLeft"]);

    assert.equal(againstWall.activePiece.x, 0);
    assert.equal(againstCell.activePiece.x, 3);
});

test("uses server-equivalent rotation corrections and predicts hard drop", () => {
    const rotated = predictArcadeArena(arena(), ["RotateClockwise"]);
    const dropped = predictArcadeArena(arena({
        settledCells: [{ x: 3, y: 19, material: "junk" }]
    }), ["InstantDrop"]);

    assert.equal(rotated.activePiece.rotation, 1);
    assert.equal(dropped.activePiece.y, 17);
});