const keyboardInputs = new Map([
    ["ArrowLeft", "MoveLeft"], ["a", "MoveLeft"],
    ["ArrowRight", "MoveRight"], ["d", "MoveRight"],
    ["ArrowUp", "RotateClockwise"], ["w", "RotateClockwise"],
    ["ArrowDown", "SoftDrop"], ["s", "SoftDrop"],
    [" ", "InstantDrop"], ["Enter", "InstantDrop"],
    ["c", "Stash"],
    ["Shift", "ActivateAbility"], ["x", "ActivateAbility"]
]);

const materialColours = {
    copper: "#e78b45", aqua: "#38d8ff", lemon: "#f6df52", violet: "#a47cff",
    coral: "#ff617d", mint: "#58e3a5", sky: "#62a8ff", sand: "#d8b978", junk: "#74798d"
};

function cellsAt(arena, piece) {
    let cells = (arena.pieceShapes?.[piece.pieceKey] ?? []).map(cell => ({ ...cell }));
    const turns = ((Number(piece.rotation) % 4) + 4) % 4;
    cells = cells.map(cell => turns === 0 ? cell
        : turns === 1 ? { x: -cell.y, y: cell.x }
        : turns === 2 ? { x: -cell.x, y: -cell.y }
        : { x: cell.y, y: -cell.x });
    const minimumX = Math.min(...cells.map(cell => cell.x));
    const minimumY = Math.min(...cells.map(cell => cell.y));
    return cells.map(cell => ({
        x: cell.x - minimumX + Number(piece.x),
        y: cell.y - minimumY + Number(piece.y)
    }));
}

function canOccupy(arena, piece) {
    const occupied = new Set((arena.settledCells ?? []).map(cell => `${cell.x}:${cell.y}`));
    const totalRows = Number(arena.visibleRows) + Number(arena.hiddenRows);
    return cellsAt(arena, piece).every(cell => cell.x >= 0 && cell.x < Number(arena.columns) &&
        cell.y >= 0 && cell.y < totalRows && !occupied.has(`${cell.x}:${cell.y}`));
}

function applyPrediction(arena, input) {
    if (!arena?.activePiece) return;
    const active = arena.activePiece;
    if (input === "MoveLeft" || input === "MoveRight" || input === "SoftDrop") {
        const candidate = { ...active,
            x: Number(active.x) + (input === "MoveLeft" ? -1 : input === "MoveRight" ? 1 : 0),
            y: Number(active.y) + (input === "SoftDrop" ? 1 : 0) };
        if (canOccupy(arena, candidate)) arena.activePiece = candidate;
    } else if (input === "RotateClockwise") {
        const corrections = [[0, 0], [-1, 0], [1, 0], [0, -1], [-2, 0], [2, 0], [-1, -1], [1, -1]];
        for (const [x, y] of corrections) {
            const candidate = { ...active, rotation: (Number(active.rotation) + 1) % 4,
                x: Number(active.x) + x, y: Number(active.y) + y };
            if (canOccupy(arena, candidate)) {
                arena.activePiece = candidate;
                break;
            }
        }
    } else if (input === "InstantDrop") {
        let candidate = { ...active };
        while (canOccupy(arena, { ...candidate, y: Number(candidate.y) + 1 })) {
            candidate = { ...candidate, y: Number(candidate.y) + 1 };
        }
        arena.activePiece = candidate;
    }
}

export function predictArcadeArena(authoritativeArena, inputs) {
    if (!authoritativeArena) return null;
    const arena = structuredClone(authoritativeArena);
    inputs.forEach(input => applyPrediction(arena, typeof input === "string" ? input : input.input));
    return arena;
}

export function create(element, connectionKey, actionKind, initialState) {
    const abort = new AbortController();
    const holds = new Map();
    const pointerActivations = new WeakSet();
    let nextSequence = Number(initialState.nextSequence ?? 0);
    let selectedTargetId = initialState.selectedTargetId ?? null;
    let disabled = Boolean(initialState.disabled);
    let authoritativeArena = initialState.arena ?? null;
    let pendingInputs = [];
    const arenaCanvas = element.querySelector("[data-arcade-arena]");
    const nextPieces = element.querySelector("[data-arcade-next]");

    const renderArena = () => {
        if (!arenaCanvas || !authoritativeArena) return;
        const arena = predictArcadeArena(authoritativeArena, pendingInputs);
        const context = arenaCanvas.getContext("2d");
        const width = arenaCanvas.width;
        const height = arenaCanvas.height;
        const cell = Math.min(width / Number(arena.columns), height / Number(arena.visibleRows));
        const left = (width - cell * Number(arena.columns)) / 2;
        const hiddenRows = Number(arena.hiddenRows);
        context.clearRect(0, 0, width, height);
        context.fillStyle = "#03050c";
        context.fillRect(0, 0, width, height);
        context.strokeStyle = "rgba(155, 234, 255, .12)";
        context.lineWidth = 1;
        for (let column = 0; column <= Number(arena.columns); column++) {
            context.beginPath();
            context.moveTo(left + column * cell, 0);
            context.lineTo(left + column * cell, cell * Number(arena.visibleRows));
            context.stroke();
        }
        for (let row = 0; row <= Number(arena.visibleRows); row++) {
            context.beginPath();
            context.moveTo(left, row * cell);
            context.lineTo(left + cell * Number(arena.columns), row * cell);
            context.stroke();
        }
        const drawCell = (item, material, active = false) => {
            const visibleY = Number(item.y) - hiddenRows;
            if (visibleY < 0 || visibleY >= Number(arena.visibleRows)) return;
            const inset = Math.max(1, cell * .08);
            context.fillStyle = materialColours[material] ?? "#d7d9e2";
            context.fillRect(left + Number(item.x) * cell + inset, visibleY * cell + inset,
                cell - inset * 2, cell - inset * 2);
            context.fillStyle = active ? "rgba(255,255,255,.42)" : "rgba(255,255,255,.2)";
            context.fillRect(left + Number(item.x) * cell + inset * 2, visibleY * cell + inset * 2,
                cell - inset * 4, Math.max(2, cell * .1));
        };
        (arena.settledCells ?? []).forEach(item => drawCell(item, item.material));
        if (arena.activePiece) {
            cellsAt(arena, arena.activePiece).forEach(item => drawCell(item, arena.activePiece.material, true));
        }
        if (nextPieces) {
            nextPieces.replaceChildren(...(arena.upcomingPieces ?? []).map(item => {
                const label = document.createElement("span");
                label.textContent = item.pieceKey.replaceAll("-", " ");
                label.style.setProperty("--piece-colour", materialColours[item.material] ?? "#d7d9e2");
                return label;
            }));
        }
    };

    const send = (input, targetOverride = null) => {
        if (disabled) return;
        const sequence = nextSequence++;
        const payload = {
            sequence,
            input,
            targetPlayerId: targetOverride ?? selectedTargetId,
            clientTimestamp: new Date().toISOString()
        };
        if (["MoveLeft", "MoveRight", "RotateClockwise", "SoftDrop", "InstantDrop"].includes(input)) {
            pendingInputs.push({ sequence, input });
            renderArena();
        }
        void window.quizizzoRealtime.send(connectionKey, "SubmitArcadeAction", [
            crypto.randomUUID(), actionKind, payload
        ]).catch(() => {});
    };

    const stopHold = key => {
        const hold = holds.get(key);
        if (!hold) return;
        clearTimeout(hold.timeout);
        clearInterval(hold.interval);
        holds.delete(key);
    };

    const startHold = (key, input, repeatMilliseconds) => {
        stopHold(key);
        send(input);
        if (!Number.isFinite(repeatMilliseconds)) return;
        const hold = { timeout: 0, interval: 0 };
        hold.timeout = setTimeout(() => {
            send(input);
            hold.interval = setInterval(() => send(input), repeatMilliseconds);
        }, 300);
        holds.set(key, hold);
    };

    element.querySelectorAll("[data-arcade-input]").forEach(button => {
        const input = button.dataset.arcadeInput;
        const parsedRepeat = Number(button.dataset.arcadeRepeat);
        const repeat = button.dataset.arcadeRepeat ? Math.max(40, Math.min(500, parsedRepeat)) : NaN;
        button.addEventListener("pointerdown", event => {
            event.preventDefault();
            pointerActivations.add(button);
            button.setPointerCapture?.(event.pointerId);
            startHold(`pointer-${event.pointerId}`, input, repeat);
        }, { signal: abort.signal });
        button.addEventListener("pointerup", event => stopHold(`pointer-${event.pointerId}`),
            { signal: abort.signal });
        button.addEventListener("pointercancel", event => stopHold(`pointer-${event.pointerId}`),
            { signal: abort.signal });
        button.addEventListener("lostpointercapture", event => stopHold(`pointer-${event.pointerId}`),
            { signal: abort.signal });
        button.addEventListener("click", event => {
            event.preventDefault();
            if (pointerActivations.delete(button)) return;
            send(input);
        }, { signal: abort.signal });
    });

    element.querySelectorAll("[data-arcade-target]").forEach(button => {
        button.addEventListener("click", () => {
            selectedTargetId = button.dataset.arcadeTarget;
            element.querySelectorAll("[data-arcade-target]").forEach(candidate => {
                const selected = candidate === button;
                candidate.classList.toggle("selected", selected);
                candidate.setAttribute("aria-pressed", String(selected));
            });
            send("SelectTarget", selectedTargetId);
        }, { signal: abort.signal });
    });

    element.addEventListener("keydown", event => {
        const input = keyboardInputs.get(event.key) ?? keyboardInputs.get(event.key.toLowerCase());
        if (!input || event.repeat || holds.has(`key-${event.key}`)) return;
        event.preventDefault();
        const button = element.querySelector(`[data-arcade-input="${input}"]`);
        const parsedRepeat = Number(button?.dataset.arcadeRepeat);
        const repeat = button?.dataset.arcadeRepeat ? Math.max(40, Math.min(500, parsedRepeat)) : NaN;
        startHold(`key-${event.key}`, input, repeat);
    }, { signal: abort.signal });
    element.addEventListener("keyup", event => {
        if (keyboardInputs.has(event.key) || keyboardInputs.has(event.key.toLowerCase())) {
            event.preventDefault();
            stopHold(`key-${event.key}`);
        }
    }, { signal: abort.signal });
    element.addEventListener("blur", () => [...holds.keys()].forEach(stopHold),
        { signal: abort.signal });

    return {
        update(state) {
            const acknowledgedSequence = Number(state.nextSequence ?? 0);
            nextSequence = Math.max(nextSequence, acknowledgedSequence);
            pendingInputs = pendingInputs.filter(item => item.sequence >= acknowledgedSequence);
            selectedTargetId = state.selectedTargetId ?? selectedTargetId;
            disabled = Boolean(state.disabled);
            authoritativeArena = state.arena ?? authoritativeArena;
            if (disabled) [...holds.keys()].forEach(stopHold);
            renderArena();
        },
        dispose() {
            abort.abort();
            [...holds.keys()].forEach(stopHold);
        }
    };
}