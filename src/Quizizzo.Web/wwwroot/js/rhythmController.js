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

export function autoplayVisualNotes(notes, maximumGapSeconds = 0.25, minimumRunLength = 3) {
    const visual = [];
    for (let lane = 0; lane < 4; lane += 1) {
        const ordered = notes
            .filter(note => Number(note.lane) === lane)
            .sort((left, right) => Number(left.startTimeSeconds) - Number(right.startTimeSeconds));
        for (let index = 0; index < ordered.length;) {
            let end = index + 1;
            while (end < ordered.length &&
                Number(ordered[end].startTimeSeconds) - Number(ordered[end - 1].startTimeSeconds) <= maximumGapSeconds) {
                end += 1;
            }
            const run = ordered.slice(index, end);
            if (run.length >= minimumRunLength) {
                const first = run[0];
                const lastEnd = Math.max(...run.map(note =>
                    Number(note.startTimeSeconds) + Number(note.durationSeconds)));
                visual.push({
                    ...first,
                    id: `visual-run-${lane}-${first.id}`,
                    durationSeconds: lastEnd - Number(first.startTimeSeconds),
                    type: "Hold"
                });
            } else {
                visual.push(...run);
            }
            index = end;
        }
    }
    return visual.sort((left, right) => Number(left.startTimeSeconds) - Number(right.startTimeSeconds) ||
        Number(left.lane) - Number(right.lane));
}

export function createRhythmController(element, connectionKey, actionKind, initialState) {
    const abort = new AbortController();
    const canvas = element.querySelector("[data-rhythm-canvas]");
    const feedback = element.querySelector("[data-rhythm-feedback]");
    const context = canvas.getContext("2d");
    const pressedPointers = new Map();
    const pressedKeys = new Set();
    const playedNotes = new Set();
    const audioBuffers = new Map();
    let audioContext = null;
    let animationFrame = 0;
    let configuration = initialState.configuration;
    let visualNotes = configuration.autoplay ? autoplayVisualNotes(configuration.notes) : configuration.notes;
    let disabled = Boolean(initialState.disabled);
    let nextSequence = Number(configuration.nextSequence ?? 1);
    let lastAutoplayPosition = songPositionSeconds(configuration.songStartsAtUtc) - 0.05;
    const tapPulseTimers = new Map();

    function ensureAudioContext() {
        const AudioContextType = globalThis.AudioContext ?? globalThis.webkitAudioContext;
        if (!AudioContextType) return null;
        if (!audioContext || audioContext.state === "closed") {
            audioContext = globalThis.quizizzoVoiceAudioContext?.state !== "closed"
                ? globalThis.quizizzoVoiceAudioContext
                : null;
            audioContext ??= new AudioContextType({ latencyHint: "interactive" });
        }
        globalThis.quizizzoVoiceAudioContext = audioContext;
        if (audioContext.state === "suspended") void audioContext.resume();
        return audioContext;
    }

    async function loadSample(assetId) {
        if (!assetId) return null;
        if (audioBuffers.has(assetId)) return audioBuffers.get(assetId);
        const activeContext = ensureAudioContext();
        if (!activeContext) return null;
        const response = await fetch(`/api/voicechoon/samples/${assetId}`, { credentials: "same-origin" });
        if (!response.ok) return null;
        const buffer = await activeContext.decodeAudioData(await response.arrayBuffer());
        audioBuffers.set(assetId, buffer);
        return buffer;
    }

    function preload() {
        const unique = new Set(configuration.notes.map(note => note.sampleAssetId).filter(Boolean));
        for (const assetId of unique) void loadSample(assetId);
    }

    async function playNote(note) {
        const activeContext = ensureAudioContext();
        const buffer = await loadSample(note.sampleAssetId);
        if (!activeContext || !buffer) return;
        const source = activeContext.createBufferSource();
        const gain = activeContext.createGain();
        source.buffer = buffer;
        source.playbackRate.value = Number(note.playbackRate);
        source.loop = Boolean(note.loop);
        if (source.loop) {
            source.loopStart = Math.min(Number(note.loopStartSeconds ?? 0), buffer.duration);
            source.loopEnd = Math.min(Number(note.loopEndSeconds ?? buffer.duration), buffer.duration);
        }
        gain.gain.setValueAtTime(0.9, activeContext.currentTime);
        gain.gain.setValueAtTime(0.9, activeContext.currentTime + Math.max(0, Number(note.durationSeconds) - 0.03));
        gain.gain.linearRampToValueAtTime(0, activeContext.currentTime + Math.max(0.03, Number(note.durationSeconds)));
        source.connect(gain).connect(activeContext.destination);
        source.start();
        source.stop(activeContext.currentTime + Math.max(0.05, Number(note.durationSeconds)) + 0.02);
    }

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
            feedback.textContent = judgeLabel(position - Number(note.startTimeSeconds));
            feedback.style.color = laneColours[lane];
            void playNote(note);
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
                void playNote(note);
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
        context.fillStyle = "#ffffff";
        context.fillRect(0, hitY, width, 5);
        for (const note of visibleNotes(visualNotes, position, travel)) {
            const delta = Number(note.startTimeSeconds) - position;
            const y = hitY - (delta / travel) * (hitY - 24);
            const x = Number(note.lane) * laneWidth + 12;
            const noteHeight = note.type === "Hold"
                ? Math.max(28, Number(note.durationSeconds) / travel * (hitY - 24))
                : 28;
            context.fillStyle = laneColours[Number(note.lane)];
            context.beginPath();
            context.roundRect(x, y - noteHeight, laneWidth - 24, noteHeight, 10);
            context.fill();
            context.fillStyle = "rgba(255,255,255,.5)";
            context.fillRect(x + 8, y - noteHeight + 6, laneWidth - 40, 4);
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

    preload();
    draw();
    return {
        update(state) {
            configuration = state.configuration;
            visualNotes = configuration.autoplay ? autoplayVisualNotes(configuration.notes) : configuration.notes;
            disabled = Boolean(state.disabled);
            nextSequence = Math.max(nextSequence, Number(configuration.nextSequence ?? 1));
            preload();
        },
        dispose() {
            abort.abort();
            tapPulseTimers.forEach(timer => window.clearTimeout(timer));
            tapPulseTimers.clear();
            cancelAnimationFrame(animationFrame);
            if (audioContext) void audioContext.close();
        }
    };
}