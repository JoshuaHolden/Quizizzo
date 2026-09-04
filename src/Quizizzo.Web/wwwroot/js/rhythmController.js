const laneColours = ["#ff4f86", "#ffd84d", "#4ce0ff", "#7cf2a5"];
const keyboardLanes = new Map([["d", 0], ["f", 1], ["j", 2], ["k", 3]]);

export function songPositionSeconds(songStartsAtUtc, nowMilliseconds = Date.now()) {
    return (nowMilliseconds - Date.parse(songStartsAtUtc)) / 1000;
}

export function visibleNotes(notes, position, travelSeconds) {
    return notes.filter(note => note.startTimeSeconds >= position - 0.25 &&
        note.startTimeSeconds <= position + travelSeconds);
}

export function nearestLaneNote(notes, lane, position, maximumDistance = 0.25) {
    return notes
        .filter(note => Number(note.lane) === lane && Math.abs(Number(note.startTimeSeconds) - position) <= maximumDistance)
        .sort((left, right) => Math.abs(Number(left.startTimeSeconds) - position) -
            Math.abs(Number(right.startTimeSeconds) - position))[0] ?? null;
}

export function dueAutoplayNotes(notes, previousPosition, position, playedNoteIds = new Set()) {
    return notes.filter(note => {
        const noteTime = Number(note.startTimeSeconds);
        return noteTime > previousPosition && noteTime <= position && !playedNoteIds.has(note.id);
    });
}

export function createRhythmController(element, connectionKey, actionKind, initialState) {
    const abort = new AbortController();
    const canvas = element.querySelector("[data-rhythm-canvas]");
    const feedback = element.querySelector("[data-rhythm-feedback]");
    const context = canvas.getContext("2d");
    const pressedPointers = new Map();
    const pressedKeys = new Set();
    const playedNotes = new Set();
    let animationFrame = 0;
    let configuration = initialState.configuration;
    let disabled = Boolean(initialState.disabled);
    let nextSequence = Number(configuration.nextSequence ?? 1);
    let lastAutoplayPosition = songPositionSeconds(configuration.songStartsAtUtc) - 0.05;
    const tapPulseTimers = new Map();
    const holdReleaseTimers = new Map();
    const laneHitEffects = new Map();

    function judgeLabel(errorSeconds) {
        const error = Math.abs(errorSeconds);
        return error <= Number(configuration.perfectWindowSeconds ?? 0.06)
            ? "PERFECT"
            : error <= Number(configuration.greatWindowSeconds ?? 0.12)
                ? "GREAT"
                : error <= Number(configuration.goodWindowSeconds ?? 0.2) ? "GOOD" : "MISS";
    }

    function activate(lane) {
        if (disabled) return null;
        const position = songPositionSeconds(configuration.songStartsAtUtc);
        const note = nearestLaneNote(
            configuration.notes,
            lane,
            position,
            Number(configuration.goodWindowSeconds ?? 0.2));
        const newNote = note && !playedNotes.has(note.id) ? note : null;
        if (newNote) {
            playedNotes.add(note.id);
            laneHitEffects.set(lane, {
                noteId: note.id,
                hold: String(note.type || "").toLowerCase() === "hold",
                released: false,
                hitAt: performance.now(),
                releasedAt: null
            });
            feedback.textContent = judgeLabel(position - Number(note.startTimeSeconds));
            feedback.style.color = laneColours[lane];
        } else {
            feedback.textContent = "TOO SOON";
            feedback.style.color = "#ff7d96";
        }
        const sequence = nextSequence++;
        void window.quizizzoRealtime.send(connectionKey, "SubmitRhythmAction", [
            crypto.randomUUID(),
            actionKind,
            { sequence, input: `Lane${lane}`, clientTimestamp: new Date().toISOString() }
        ]).catch(() => { });
        return newNote;
    }

    function showPadFeedback(lane, note) {
        const button = element.querySelector(`[data-rhythm-lane="${lane}"]`);
        if (!button || !note) return;
        if (note.type === "Hold") {
            button.classList.add("hold-active");
            return;
        }
        button.classList.remove("tap-hit");
        void button.offsetWidth;
        button.classList.add("tap-hit");
        window.clearTimeout(tapPulseTimers.get(lane));
        tapPulseTimers.set(lane, window.setTimeout(() => {
            button.classList.remove("tap-hit");
            tapPulseTimers.delete(lane);
        }, 240));
    }

    function releasePadFeedback(lane) {
        element.querySelector(`[data-rhythm-lane="${lane}"]`)?.classList.remove("hold-active");
        const effect = laneHitEffects.get(lane);
        if (effect?.hold) {
            effect.released = true;
            effect.releasedAt = performance.now();
            holdReleaseTimers.set(lane, window.setTimeout(() => {
                holdReleaseTimers.delete(lane);
                const sequence = nextSequence++;
                void window.quizizzoRealtime.send(connectionKey, "SubmitRhythmAction", [
                    crypto.randomUUID(), actionKind,
                    { sequence, input: `Lane${lane}`, released: true, clientTimestamp: new Date().toISOString() }
                ]).catch(() => { });
            }, 100));
            return;
        }
    }

    function resumeInterruptedHold(lane) {
        const timer = holdReleaseTimers.get(lane);
        if (timer === undefined) return false;
        window.clearTimeout(timer);
        holdReleaseTimers.delete(lane);
        const effect = laneHitEffects.get(lane);
        if (effect) {
            effect.released = false;
            effect.releasedAt = null;
        }
        element.querySelector(`[data-rhythm-lane="${lane}"]`)?.classList.add("hold-active");
        return true;
    }

    function draw() {
        const width = canvas.width;
        const height = canvas.height;
        const hitY = height - 72;
        const laneWidth = width / 4;
        const position = songPositionSeconds(configuration.songStartsAtUtc);
        const travel = Number(configuration.noteTravelSeconds ?? 2);
        if (configuration.autoplay) {
            for (const note of dueAutoplayNotes(configuration.notes, lastAutoplayPosition, position, playedNotes)) {
                playedNotes.add(note.id);
            }
            lastAutoplayPosition = position;
        }
        context.clearRect(0, 0, width, height);
        context.fillStyle = "#050719";
        context.fillRect(0, 0, width, height);
        for (let lane = 0; lane < 4; lane += 1) {
            context.fillStyle = `${laneColours[lane]}12`;
            context.fillRect(lane * laneWidth, 0, laneWidth, height);
            context.strokeStyle = `${laneColours[lane]}55`;
            context.beginPath();
            context.moveTo(lane * laneWidth, 0);
            context.lineTo(lane * laneWidth, height);
            context.stroke();
        }
        const now = performance.now();
        context.fillStyle = "rgba(255,255,255,.2)";
        context.fillRect(0, hitY - 8, width, 21);
        context.fillStyle = "#ffffff";
        context.fillRect(0, hitY, width, 5);
        context.fillStyle = "#d8e7f2";
        context.font = "700 16px Fredoka, sans-serif";
        context.textAlign = "center";
        context.fillText("PRESS / HOLD", width / 2, hitY - 14);
        for (const note of visibleNotes(configuration.notes, position, travel)) {
            const delta = Number(note.startTimeSeconds) - position;
            const y = hitY - (delta / travel) * (hitY - 24);
            const x = Number(note.lane) * laneWidth + 12;
            const noteHeight = note.type === "Hold"
                ? Math.max(28, Number(note.durationSeconds) / travel * (hitY - 24))
                : 28;
            const effect = laneHitEffects.get(Number(note.lane));
            const isHit = effect?.noteId === note.id;
            let alpha = 1;
            if (isHit && !effect.released) {
                alpha = effect.hold ? 1 : Math.max(0, 1 - (now - effect.hitAt) / 280);
            } else if (isHit && effect.releasedAt !== null) {
                alpha = Math.max(0, 1 - (now - effect.releasedAt) / 280);
            }
            if (isHit && alpha <= 0) {
                laneHitEffects.delete(Number(note.lane));
                continue;
            }
            context.save();
            context.globalAlpha = alpha;
            if (isHit) {
                context.shadowColor = laneColours[Number(note.lane)];
                context.shadowBlur = effect.hold && !effect.released ? 24 : 14;
            }
            context.fillStyle = laneColours[Number(note.lane)];
            context.beginPath();
            context.roundRect(x, y - noteHeight, laneWidth - 24, noteHeight, 10);
            context.fill();
            context.restore();
            context.fillStyle = "rgba(255,255,255,.5)";
            context.globalAlpha = alpha;
            context.fillRect(x + 8, y - noteHeight + 6, laneWidth - 40, 4);
            context.globalAlpha = 1;
            if (configuration.autoplay && note.soundLabel) {
                context.save();
                context.globalAlpha = Math.min(1, alpha);
                context.fillStyle = "#f4fbff";
                context.font = "600 12px Nunito, sans-serif";
                context.textAlign = "left";
                context.fillText(note.soundLabel, x + 12, Math.max(18, y - noteHeight - 7));
                context.restore();
            }
        }
        drawProgress(context, width, height, position, configuration.songDurationSeconds);
        animationFrame = requestAnimationFrame(draw);
    }

    function drawProgress(context, width, height, position, songDurationSeconds) {
        const progress = Math.max(0, Math.min(1, position / Number(songDurationSeconds)));
        context.fillStyle = "rgba(255,255,255,.18)";
        context.fillRect(0, height - 8, width, 8);
        context.fillStyle = "#4ce0ff";
        context.fillRect(0, height - 8, width * progress, 8);
    }

    element.querySelectorAll("[data-rhythm-lane]").forEach(button => {
        const lane = Number(button.dataset.rhythmLane);
        button.addEventListener("pointerdown", event => {
            event.preventDefault();
            button.setPointerCapture?.(event.pointerId);
            pressedPointers.set(event.pointerId, lane);
            button.classList.add("pressed");
            if (resumeInterruptedHold(lane)) return;
            showPadFeedback(lane, activate(lane));
        }, { signal: abort.signal });
        const release = event => {
            if (pressedPointers.delete(event.pointerId)) {
                button.classList.remove("pressed");
                releasePadFeedback(lane);
            }
        };
        button.addEventListener("pointerup", release, { signal: abort.signal });
        button.addEventListener("pointercancel", release, { signal: abort.signal });
        button.addEventListener("lostpointercapture", release, { signal: abort.signal });
    });

    element.addEventListener("keydown", event => {
        const key = event.key.toLowerCase();
        const lane = keyboardLanes.get(key);
        if (lane === undefined || event.repeat || pressedKeys.has(key)) return;
        event.preventDefault();
        pressedKeys.add(key);
        element.querySelector(`[data-rhythm-lane="${lane}"]`)?.classList.add("pressed");
        if (resumeInterruptedHold(lane)) return;
        showPadFeedback(lane, activate(lane));
    }, { signal: abort.signal });
    element.addEventListener("keyup", event => {
        const key = event.key.toLowerCase();
        const lane = keyboardLanes.get(key);
        if (lane === undefined) return;
        pressedKeys.delete(key);
        element.querySelector(`[data-rhythm-lane="${lane}"]`)?.classList.remove("pressed");
        releasePadFeedback(lane);
    }, { signal: abort.signal });

    draw();
    return {
        update(state) {
            configuration = state.configuration;
            disabled = Boolean(state.disabled);
            nextSequence = Math.max(nextSequence, Number(configuration.nextSequence ?? 1));
        },
        dispose() {
            abort.abort();
            tapPulseTimers.forEach(timer => window.clearTimeout(timer));
            tapPulseTimers.clear();
            holdReleaseTimers.forEach(timer => window.clearTimeout(timer));
            holdReleaseTimers.clear();
            laneHitEffects.clear();
            cancelAnimationFrame(animationFrame);
        }
    };
}
