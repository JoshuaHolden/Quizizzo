window.quizizzoPresentation = (() => {
    const presentations = new Map();
    const width = 1280;
    const height = 720;

    function renderResolution(parent) {
        const deviceScale = Math.max(1, window.devicePixelRatio || 1);
        const displayScale = Math.max(
            1,
            parent.clientWidth / width,
            parent.clientHeight / height);
        return Math.min(3, deviceScale * displayScale);
    }

    const displayFont = '"Quizizzo Display", "Arial Rounded MT Bold", sans-serif';
    const bodyFont = '"Quizizzo Sans", "Segoe UI", sans-serif';

    function cloneSnapshot(snapshot) {
        return JSON.parse(JSON.stringify(snapshot));
    }

    function playerMap(snapshot) {
        return new Map((snapshot?.players || []).map(player => [player.playerId, player]));
    }

    function field(source, name, fallback = null) {
        if (!source) return fallback;
        const pascalName = `${name.charAt(0).toUpperCase()}${name.slice(1)}`;
        return source[name] ?? source[pascalName] ?? fallback;
    }

    function normalizedId(value) {
        return String(value || "").replaceAll("-", "").toLowerCase();
    }

    function createVoiceChoonDisplayAudio() {
        let audioContext = null;
        let output = null;
        let recordingDestination = null;
        let snapshot = null;
        let muted = localStorage.getItem("quizizzo.display.audio-muted") === "true";
        let schedulerTimer = null;
        let sources = new Set();
        let scheduledNoteIds = new Set();
        let missedJudgementIds = new Set();
        let soundedMissIds = new Set();
        let scheduleKey = null;
        const buffers = new Map();
        let countdownBuffer = null;

        function ensureContext() {
            const AudioContextType = globalThis.AudioContext ?? globalThis.webkitAudioContext;
            if (!AudioContextType) return null;
            audioContext ??= new AudioContextType({ latencyHint: "interactive" });
            if (audioContext.state === "suspended") void audioContext.resume();
            if (!output) {
                output = audioContext.createDynamicsCompressor();
                output.threshold.value = -18;
                output.knee.value = 18;
                output.ratio.value = 4;
                output.attack.value = 0.01;
                output.release.value = 0.18;
                output.connect(audioContext.destination);
                recordingDestination = audioContext.createMediaStreamDestination();
                output.connect(recordingDestination);
            }
            return audioContext;
        }

        const gestureHandler = () => {
            if (!muted) ensureContext();
        };
        document.addEventListener("pointerdown", gestureHandler, { passive: true });

        async function load(assetId) {
            if (buffers.has(assetId)) return buffers.get(assetId);
            const context = ensureContext();
            if (!context) return null;
            const pending = (async () => {
                const sampleBase = snapshot?.voiceSampleBaseUrl || "/api/voicechoon/display-samples";
                const response = await fetch(`${sampleBase}/${assetId}`, { credentials: "same-origin" });
                if (!response.ok) return null;
                return context.decodeAudioData(await response.arrayBuffer());
            })().catch(() => null);
            buffers.set(assetId, pending);
            return pending;
        }

        async function playCountdownBlip() {
            if (muted) return;
            const context = ensureContext();
            if (!context || context.state !== "running") return;
            countdownBuffer ??= fetch("/assets/audio/voicechoon-countdown-blip.wav", {
                credentials: "same-origin"
            }).then(response => response.ok ? response.arrayBuffer() : null)
                .then(bytes => bytes ? context.decodeAudioData(bytes) : null)
                .catch(() => null);
            const buffer = await countdownBuffer;
            if (!buffer || muted) return;
            const source = context.createBufferSource();
            const gain = context.createGain();
            gain.gain.value = 0.7;
            source.buffer = buffer;
            source.connect(gain);
            gain.connect(output);
            source.start();
        }

        function stop() {
            if (schedulerTimer !== null) window.clearInterval(schedulerTimer);
            schedulerTimer = null;
            sources.forEach(voice => {
                try {
                    const now = audioContext?.currentTime ?? 0;
                    voice.gain.gain.cancelScheduledValues(now);
                    voice.gain.gain.setValueAtTime(Math.max(0.0001, voice.gain.gain.value), now);
                    voice.gain.gain.exponentialRampToValueAtTime(0.0001, now + 0.025);
                    voice.source.stop(now + 0.03);
                } catch { }
            });
            sources = new Set();
            scheduledNoteIds = new Set();
            missedJudgementIds = new Set();
            soundedMissIds = new Set();
            scheduleKey = null;
        }

        function sampleGain(buffer) {
            const channel = buffer.getChannelData(0);
            let sum = 0;
            const stride = Math.max(1, Math.floor(channel.length / 2000));
            for (let index = 0; index < channel.length; index += stride) sum += channel[index] * channel[index];
            const rms = Math.sqrt(sum / Math.ceil(channel.length / stride));
            return Math.max(0.55, Math.min(1.6, 0.55 / Math.max(0.08, rms)));
        }

        function snapToZeroCrossing(buffer, seconds) {
            const channel = buffer.getChannelData(0);
            const centre = Math.max(0, Math.min(channel.length - 2, Math.round(seconds * buffer.sampleRate)));
            const window = Math.min(Math.round(buffer.sampleRate * 0.02), channel.length - 1);
            for (let offset = 0; offset <= window; offset++) {
                for (const index of [centre + offset, centre - offset]) {
                    if (index >= 0 && index + 1 < channel.length &&
                        channel[index] <= 0 && channel[index + 1] >= 0) {
                        return index / buffer.sampleRate;
                    }
                }
            }
            return seconds;
        }

        async function play(note, when, offset = 0, offKeyCents = 0) {
            if (muted) return;
            const context = ensureContext();
            const buffer = await load(note.sampleAssetId);
            if (!context || !buffer || muted) return;
            const source = context.createBufferSource();
            const gain = context.createGain();
            const duration = Math.max(0.05, Number(note.durationSeconds) - offset);
            source.buffer = buffer;
            source.playbackRate.value = Number(note.playbackRate || 1);
            source.detune.value = Number(offKeyCents || 0);
            source.loop = Boolean(note.loop) || String(note.type || "").toLowerCase() === "hold";
            if (source.loop) {
                // Derive the loop from the real decoded recording. Older snapshots used a
                // fictional one-second duration, which produced invalid loop boundaries.
                const loopStart = snapToZeroCrossing(buffer, buffer.duration * 0.3);
                const loopEnd = snapToZeroCrossing(buffer, buffer.duration * 0.7);
                source.loopStart = loopStart;
                source.loopEnd = loopEnd > loopStart + 0.01 ? loopEnd : buffer.duration;
            }
            const velocity = Math.max(1, Math.min(127, Number(note.velocity ?? note.Velocity ?? 100)));
            const expression = Math.max(.28, Math.sqrt(velocity / 127));
            const level = Math.min(0.42, sampleGain(buffer) * 0.35 * expression);
            const startAt = Math.max(context.currentTime + 0.005, when);
            gain.gain.setValueAtTime(0, startAt);
            gain.gain.linearRampToValueAtTime(level, startAt + 0.015);
            gain.gain.setValueAtTime(level, startAt + Math.max(0.015, duration - 0.05));
            gain.gain.linearRampToValueAtTime(0.0001, startAt + duration);
            source.connect(gain).connect(output);
            const voice = { source, gain };
            sources.add(voice);
            source.addEventListener("ended", () => sources.delete(voice), { once: true });
            source.start(startAt, source.loop ? 0 : Math.min(offset, Math.max(0, buffer.duration - 0.01)));
            source.stop(startAt + duration + 0.03);
        }

        function schedule() {
            if (muted || !snapshot || snapshot.gameKey !== "voicechoon" || snapshot.phase !== "Playing") return;
            const state = snapshot.gameState || {};
            const notes = state.playback || state.Playback || [];
            const startsAt = snapshot.gameState?.songStartsAtUtc || snapshot.gameState?.SongStartsAtUtc;
            const key = `${snapshot.gameInstanceId || "voicechoon"}:${startsAt}`;
            if (scheduleKey === key && schedulerTimer !== null) return;
            stop();
            scheduleKey = key;
            const context = ensureContext();
            if (!context || !startsAt) return;
            // Decode everything during the lead-in rather than on the note boundary.
            void Promise.all([...new Set(notes.map(note => note.sampleAssetId ?? note.SampleAssetId))].map(load));
            const tick = () => {
                const songPosition = (Date.now() - Date.parse(startsAt)) / 1000;
                const audioOrigin = context.currentTime - songPosition;
                const liveState = snapshot?.gameState || {};
                const performers = liveState.performers || liveState.Performers || [];
                const judgedIds = new Set(performers.flatMap(performer =>
                    performer.judgedNoteIds || performer.JudgedNoteIds || []).map(normalizedId));
                const judgementNotes = performers.flatMap(performer =>
                    performer.notes || performer.Notes || []);
                judgementNotes.forEach(judgement => {
                    const judgementId = normalizedId(judgement.noteId ?? judgement.NoteId);
                    const judgementTime = Number(judgement.startTimeSeconds ?? judgement.StartTimeSeconds);
                    if (!judgementId || judgedIds.has(judgementId) || missedJudgementIds.has(judgementId) ||
                        judgementTime + .65 >= songPosition) return;
                    missedJudgementIds.add(judgementId);
                    const sourSource = notes.find(note => normalizedId(
                        note.judgementNoteId ?? note.JudgementNoteId) === judgementId);
                    if (!sourSource || soundedMissIds.has(judgementId)) return;
                    soundedMissIds.add(judgementId);
                    const sourDirection = Number.parseInt(judgementId.slice(-2), 16) % 2 ? 1 : -1;
                    void play({
                        sampleAssetId: sourSource.sampleAssetId ?? sourSource.SampleAssetId,
                        playbackRate: sourSource.playbackRate ?? sourSource.PlaybackRate,
                        durationSeconds: Math.min(.32, Number(
                            sourSource.durationSeconds ?? sourSource.DurationSeconds ?? .25)),
                        loop: false
                    }, context.currentTime + .005, 0, sourDirection * 175);
                });
                notes.forEach(note => {
                    const id = String(note.id ?? note.Id);
                    if (scheduledNoteIds.has(id)) return;
                    const start = Number(note.startTimeSeconds ?? note.StartTimeSeconds);
                    const duration = Number(note.durationSeconds ?? note.DurationSeconds);
                    const end = start + duration;
                    if (end <= songPosition) {
                        scheduledNoteIds.add(id);
                        return;
                    }
                    if (start > songPosition + 0.3) return;
                    scheduledNoteIds.add(id);
                    const judgementId = normalizedId(note.judgementNoteId ?? note.JudgementNoteId);
                    const offKey = missedJudgementIds.has(judgementId);
                    const sourDirection = Number.parseInt(judgementId.slice(-2), 16) % 2 ? 1 : -1;
                    void play({
                        sampleAssetId: note.sampleAssetId ?? note.SampleAssetId,
                        playbackRate: note.playbackRate ?? note.PlaybackRate,
                        durationSeconds: note.durationSeconds ?? note.DurationSeconds,
                        loop: note.loop ?? note.Loop,
                        loopStartSeconds: note.loopStartSeconds ?? note.LoopStartSeconds,
                        loopEndSeconds: note.loopEndSeconds ?? note.LoopEndSeconds,
                        velocity: note.velocity ?? note.Velocity,
                        type: note.type ?? note.Type
                    }, audioOrigin + Math.max(start, songPosition), Math.max(0, songPosition - start),
                    offKey ? sourDirection * 175 : 0);
                });
            };
            tick();
            schedulerTimer = window.setInterval(tick, 50);
        }

        return {
            update(nextSnapshot) {
                snapshot = nextSnapshot;
                if (snapshot?.gameKey === "voicechoon" && snapshot.phase === "Playing") {
                    ensureContext();
                    schedule();
                } else stop();
            },
            setMuted(value) {
                muted = Boolean(value);
                if (muted) stop();
                else schedule();
            },
            destroy() {
                stop();
                document.removeEventListener("pointerdown", gestureHandler);
                audioContext?.close();
                audioContext = null;
            },
            recordingStream() {
                ensureContext();
                return recordingDestination?.stream || null;
            },
            playCountdownBlip
        };
    }

    function preferredReplayMimeType() {
        const choices = [
            "video/mp4;codecs=h264,aac",
            "video/mp4",
            "video/webm;codecs=vp9,opus",
            "video/webm;codecs=vp8,opus",
            "video/webm"
        ];
        return choices.find(type => globalThis.MediaRecorder?.isTypeSupported?.(type)) || "";
    }

    function startReplayRecording(controller) {
        if (!controller.snapshot?.captureReplay || controller.replayRecorder ||
            !globalThis.MediaRecorder || !controller.game?.canvas?.captureStream) return;
        try {
            const stream = controller.game.canvas.captureStream(30);
            const audioStream = controller.voiceAudio?.recordingStream();
            audioStream?.getAudioTracks().forEach(track => stream.addTrack(track));
            const mimeType = preferredReplayMimeType();
            const recorder = new MediaRecorder(stream, mimeType ? {
                mimeType,
                videoBitsPerSecond: 1_600_000,
                audioBitsPerSecond: 96_000
            } : undefined);
            const chunks = [];
            recorder.addEventListener("dataavailable", event => {
                if (event.data?.size) chunks.push(event.data);
            });
            recorder.addEventListener("stop", () => {
                controller.replayBlob = new Blob(chunks, { type: recorder.mimeType || mimeType || "video/webm" });
                controller.replayRecorder = null;
                stream.getTracks().forEach(track => track.stop());
                const extension = controller.replayBlob.type.includes("mp4") ? "mp4" : "webm";
                const file = new File([controller.replayBlob], `quizizzo-voicechoon.${extension}`,
                    { type: controller.replayBlob.type });
                const canShare = Boolean(navigator.share && navigator.canShare?.({ files: [file] }));
                controller.dotNetReference?.invokeMethodAsync("ReplayReady", canShare).catch(() => { });
            }, { once: true });
            controller.replayRecorder = recorder;
            recorder.start(1000);
        } catch (error) {
            console.warn("VoiceChoon replay recording is unavailable.", error);
        }
    }

    function finishReplayRecording(controller) {
        if (controller.replayRecorder?.state === "recording") controller.replayRecorder.stop();
    }

    function laneColoursForVoice(index) {
        return ["#ff7aa4", "#ffe36d", "#67e8f9", "#86efac"][index % 4];
    }

    function mixColour(from, to, amount) {
        const mix = channel => Math.round(
            ((from >> channel) & 0xff) * (1 - amount) + ((to >> channel) & 0xff) * amount);
        return (mix(16) << 16) | (mix(8) << 8) | mix(0);
    }

    function stableVisualSeed(value) {
        let hash = 2166136261;
        for (const character of String(value || "voicechoon")) {
            hash ^= character.charCodeAt(0);
            hash = Math.imul(hash, 16777619);
        }
        return hash >>> 0;
    }

    function drawVoiceChoonMarbling(graphics, elapsed, pulse = 0, songProgress = 0, seed = 0) {
        const availablePalettes = [
            [0xf6c637, 0xef8db5, 0xf15d5d, 0x7ea3d3, 0xf4eadc, 0xd681b5, 0xf6c637],
            [0x9ad8cf, 0xc9a7eb, 0xf49ac2, 0x789dda, 0xffe49b, 0x94d2e6, 0xc9a7eb],
            [0xb7df72, 0xffa77a, 0xf56fa1, 0x77c9c2, 0xffefaa, 0xa993db, 0xb7df72],
            [0xffcf4a, 0xff82b2, 0xff7167, 0x6ecce8, 0xfff1cf, 0xca8de0, 0xffcf4a]
        ];
        const paletteOffset = seed % availablePalettes.length;
        const palettes = availablePalettes.map((_, index) =>
            availablePalettes[(index + paletteOffset) % availablePalettes.length]);
        const palettePosition = Math.max(0, Math.min(1, songProgress)) * (palettes.length - 1);
        const paletteIndex = Math.min(palettes.length - 2, Math.floor(palettePosition));
        const paletteMix = palettePosition - paletteIndex;
        const colours = palettes[paletteIndex].map((colour, index) =>
            mixColour(colour, palettes[paletteIndex + 1][index], paletteMix));
        const bands = colours.length;
        const samples = 32;
        const boundary = (index, y) => {
            if (index <= 0) return -80;
            if (index >= bands) return width + 80;
            const normalizedY = y / height;
            const base = index * width / bands;
            const broadWave = Math.sin(normalizedY * Math.PI * 2.15 + elapsed * .18 + index * .72) * 155;
            const curl = Math.sin(normalizedY * Math.PI * 4.6 - elapsed * .11 + index * 1.37) * 66;
            const breathing = Math.sin(elapsed * .55 + index) * (15 + pulse * 13);
            return base + broadWave + curl + breathing;
        };
        graphics.clear();
        for (let band = 0; band < bands; band++) {
            const points = [];
            for (let sample = 0; sample <= samples; sample++) {
                const y = sample * height / samples;
                points.push({ x: boundary(band, y), y });
            }
            for (let sample = samples; sample >= 0; sample--) {
                const y = sample * height / samples;
                points.push({ x: boundary(band + 1, y), y });
            }
            graphics.fillStyle(colours[band], 1);
            graphics.fillPoints(points, true);
            graphics.lineStyle(3 + pulse * 1.5, 0x281a2b, .88);
            graphics.strokePoints(points, true);
        }
    }

    function drawVoiceChoonFractal(graphics, elapsed, pulse = 0, songProgress = 0, seed = 0) {
        const symmetry = 5 + (seed % 5);
        const depth = 5 + ((seed >>> 4) % 2);
        const direction = (seed & 1) ? 1 : -1;
        const speed = .12 + ((seed >>> 7) % 7) * .012;
        const centreX = width * (.5 + Math.sin(seed * .000013) * .08);
        const centreY = height * (.48 + Math.cos(seed * .000017) * .06);
        const beatExpansion = 1 + pulse * .075;
        const zoomPhase = (elapsed * (.045 + ((seed >>> 13) % 5) * .004)) % 1;
        const continuousZoom = .72 + zoomPhase * .82;
        const baseAngle = elapsed * speed * direction + songProgress * Math.PI * .8;
        const hueBase = (elapsed * .035 * direction + (seed % 997) / 997 + songProgress * .42) % 1;
        const colourAt = (branch, level) => Phaser.Display.Color.HSVToRGB(
            (hueBase + branch / symmetry + level * .075 + 1) % 1,
            .54 + pulse * .18,
            .92).color;
        graphics.clear();
        graphics.fillStyle(0x10051f, 1);
        graphics.fillRect(0, 0, width, height);

        const branch = (x, y, length, angle, level, arm) => {
            if (level <= 0 || length < 4) return;
            const wobble = Math.sin(elapsed * .42 + level * 1.7 + arm) * .15;
            const endX = x + Math.cos(angle + wobble) * length * beatExpansion;
            const endY = y + Math.sin(angle + wobble) * length * beatExpansion;
            const colour = colourAt(arm, depth - level);
            graphics.lineStyle(Math.max(1.4, level * 2.15), colour, .38 + level / depth * .42);
            graphics.lineBetween(x, y, endX, endY);
            graphics.fillStyle(colour, .2 + pulse * .13);
            graphics.fillCircle(endX, endY, Math.max(2, level * 2.8 + pulse * 4));
            const fork = .43 + Math.sin(seed * .0001 + level) * .07;
            branch(endX, endY, length * .72, angle + fork, level - 1, arm);
            branch(endX, endY, length * .67, angle - fork * .84, level - 1, arm + .35);
        };

        for (let arm = 0; arm < symmetry; arm++) {
            const angle = baseAngle + arm * Math.PI * 2 / symmetry;
            branch(centreX, centreY,
                (116 + ((seed >>> 10) % 44)) * continuousZoom, angle, depth, arm);
        }
        graphics.lineStyle(3 + pulse * 5, colourAt(0, 0), .5);
        graphics.strokeCircle(centreX, centreY, 26 + pulse * 20);
    }

    function drawVoiceChoonMandelbrot(texture, elapsed, pulse = 0, songProgress = 0, seed = 0) {
        const canvas = texture.getSourceImage();
        const context = canvas.getContext("2d", { alpha: false });
        const image = context.createImageData(canvas.width, canvas.height);
        const pixels = image.data;
        const landmarks = [
            [-.743643887037151, .13182590420533],
            [-1.25066, .02012],
            [-.7453, .1127],
            [-.1011, .9563]
        ];
        const landmark = landmarks[(seed >>> 5) % landmarks.length];
        const direction = (seed & 1) ? 1 : -1;
        const rotation = elapsed * (.055 + ((seed >>> 9) % 7) * .006) * direction;
        const cosRotation = Math.cos(rotation);
        const sinRotation = Math.sin(rotation);
        const zoomDuration = 24 + ((seed >>> 16) % 12);
        const zoomPhase = (elapsed % zoomDuration) / zoomDuration;
        const continuousZoom = Math.pow(34, zoomPhase);
        const viewWidth = 3.15 / continuousZoom;
        const aspect = canvas.height / canvas.width;
        const iterations = 38 + Math.round(pulse * 8);
        const hueChase = elapsed * .045 * direction + songProgress * .55 + (seed % 997) / 997;

        for (let y = 0; y < canvas.height; y++) {
            const normalizedY = (y / (canvas.height - 1) - .5) * viewWidth * aspect;
            for (let x = 0; x < canvas.width; x++) {
                const normalizedX = (x / (canvas.width - 1) - .5) * viewWidth;
                const real = landmark[0] + normalizedX * cosRotation - normalizedY * sinRotation;
                const imaginary = landmark[1] + normalizedX * sinRotation + normalizedY * cosRotation;
                let zr = 0;
                let zi = 0;
                let iteration = 0;
                while (zr * zr + zi * zi <= 4 && iteration < iterations) {
                    const nextReal = zr * zr - zi * zi + real;
                    zi = 2 * zr * zi + imaginary;
                    zr = nextReal;
                    iteration++;
                }
                const offset = (y * canvas.width + x) * 4;
                if (iteration === iterations) {
                    pixels[offset] = 10;
                    pixels[offset + 1] = 3;
                    pixels[offset + 2] = 25;
                } else {
                    const colour = Phaser.Display.Color.HSVToRGB(
                        (hueChase + iteration / iterations * .82 + 1) % 1,
                        .72,
                        .62 + pulse * .25);
                    pixels[offset] = colour.r;
                    pixels[offset + 1] = colour.g;
                    pixels[offset + 2] = colour.b;
                }
                pixels[offset + 3] = 255;
            }
        }
        context.putImageData(image, 0, 0);
        texture.refresh();
    }

    class PartyPresentationScene extends Phaser.Scene {
        constructor(controller) {
            super({ key: `party-presentation-${controller.key}` });
            this.controller = controller;
            this.avatars = new Map();
            this.previous = null;
            this.background = null;
            this.drawingContainer = null;
            this.drawingTimer = null;
            this.drawingSignature = null;
            this.podiumContainer = null;
            this.podiumSignature = null;
            this.presenterContainer = null;
            this.presenterRig = null;
            this.presenterMessage = null;
            this.presenterPhase = null;
            this.roundRankingSignature = null;
            this.roundRankingTimer = null;
            this.roundRankingScoreTimers = [];
            this.roundRankingScoreTweens = [];
            this.roundRankingStartScores = new Map();
            this.tutorialContainer = null;
            this.tutorialSignature = null;
            this.phaseChrome = null;
            this.screenChromeContainer = null;
            this.screenChromeSignature = null;
            this.deadlineTimer = null;
            this.qrLoadPending = null;
            this.pileContainer = null;
            this.pileSignature = null;
            this.pileDeadlineTimer = null;
            this.pileCountdownTimer = null;
            this.pileEffectSignature = null;
            this.voiceContainer = null;
            this.voiceSignature = null;
            this.voiceTimer = null;
        }

        preload() {
            window.quizizzoCharacterRig.loadAtlases(this, "player-");
        }

        create() {
            this.cameras.main
                .setOrigin(0, 0)
                .setZoom(this.controller.renderResolution)
                .setScroll(0, 0);
            this.createBackground();
            this.createParticleTexture();
            this.controller.scene = this;
            this.applySnapshot(this.controller.snapshot, true);
            this.controller.readyResolve?.();
            this.controller.readyResolve = null;
        }

        createBackground() {
            this.background = this.add.graphics();
            this.drawBackground(null);

            if (this.controller.reducedMotion) {
                return;
            }

            for (let index = 0; index < 14; index++) {
                const orb = this.add.circle(
                    Phaser.Math.Between(0, width),
                    Phaser.Math.Between(0, height),
                    Phaser.Math.Between(8, 32),
                    index % 2 === 0 ? 0xffffff : 0xfde68a,
                    Phaser.Math.FloatBetween(0.025, 0.09));
                this.tweens.add({
                    targets: orb,
                    x: Phaser.Math.Between(0, width),
                    y: Phaser.Math.Between(0, height),
                    duration: Phaser.Math.Between(6000, 12000),
                    yoyo: true,
                    repeat: -1,
                    ease: "Sine.easeInOut"
                });
            }
        }

        drawBackground(gameKey, phase) {
            const briefing = gameKey === "animates" && ["Briefing", "ShowdownBriefing"].includes(phase);
            const showdown = gameKey === "animates" && phase?.startsWith("Showdown");
            const drawing = gameKey === "animates" && phase === "Drawing";
            const choosing = gameKey === "animates" && ["Choosing", "ShowdownVoting"].includes(phase);
            const results = gameKey === "animates"
                && ["Results", "ShowdownResults", "FinalCelebration"].includes(phase);
            const slop = gameKey === "slop-machine";
            const pileUp = gameKey === "pile-up-panic";
            const palette = pileUp
                ? phase === "WinnerCelebration"
                    ? [0x080914, 0x51248a, 0xffb11b]
                    : [0x080914, 0x102d4f, 0x6a2a78]
                : slop
                    ? phase?.includes("ScoreReview") || phase === "WinnerCelebration"
                        ? [0x120507, 0x7a160c, 0xffd400]
                        : [0x071519, 0x7d1616, 0x00e7d7]
                    : briefing
                        ? [0x071a2e, 0x0f766e, 0x7c3aed]
                        : results ? [0x2e1065, 0x7c2d92, 0xf59e0b]
                            : choosing ? [0x111827, 0x312e81, 0xdb2777]
                                : drawing ? [0x082f49, 0x0e7490, 0xf97316]
                                    : showdown ? [0x2e1065, 0x86198f, 0x0891b2]
                                        : gameKey === "estimate"
                                            ? [0x160b32, 0x39156b, 0x7132a8]
                                            : [0x101735, 0x272a68, 0x513487];
            this.background.clear();
            this.background.fillGradientStyle(
                palette[1], palette[2], palette[0], palette[1], 1);
            this.background.fillRect(0, 0, width, height);
            if (pileUp) {
                this.background.fillStyle(0x03050b, .44);
                this.background.fillRect(0, 0, width, height);
                this.background.lineStyle(2, 0x63e6ff, .1);
                for (let x = 32; x < width; x += 64) {
                    this.background.lineBetween(x, 0, x, height);
                }
                for (let y = 32; y < height; y += 64) {
                    this.background.lineBetween(0, y, width, y);
                }
                this.background.lineStyle(5, 0xff4fa3, .28);
                this.background.strokeRoundedRect(20, 20, width - 40, height - 40, 26);
            } else if (slop) {
                this.background.lineStyle(3, 0xffd400, .12);
                for (let x = -80; x < width; x += 110) {
                    this.background.lineBetween(x, 0, x + 220, height);
                }
                this.background.fillStyle(0x00e7d7, .12);
                this.background.fillRect(0, 610, width, 16);
                for (let x = 20; x < width; x += 70) {
                    this.background.fillCircle(x, 618, 9);
                }
                this.background.lineStyle(5, 0xffd400, .3);
                this.background.strokeRoundedRect(24, 24, width - 48, height - 48, 26);
            } else if (briefing) {
                this.background.fillStyle(0x22d3ee, .08);
                for (let x = -120; x < width; x += 130) {
                    this.background.fillTriangle(x, height, x + 260, height, x + 130, 0);
                }
                this.background.lineStyle(2, 0xffffff, .08);
                for (let y = 80; y < height; y += 80) this.background.lineBetween(0, y, width, y);
                for (let x = 80; x < width; x += 80) this.background.lineBetween(x, 0, x, height);
                this.background.fillStyle(0xfacc15, .18);
                this.background.fillCircle(110, 105, 72);
                this.background.fillCircle(1170, 610, 110);
            } else if (gameKey === "animates") {
                this.background.lineStyle(3, 0xffffff, .055);
                for (let offset = -height; offset < width; offset += 120) {
                    this.background.lineBetween(offset, height, offset + height, 0);
                }
                this.background.fillStyle(results ? 0xfde68a : 0x67e8f9, .09);
                this.background.fillCircle(85, 620, 150);
                this.background.fillCircle(1190, 90, 125);
                this.background.lineStyle(5, 0xffffff, .07);
                this.background.strokeRoundedRect(32, 32, width - 64, height - 64, 30);
            }
        }

        createParticleTexture() {
            const textureKey = `confetti-${this.controller.key}`;
            const graphic = this.make.graphics({ x: 0, y: 0, add: false });
            graphic.fillStyle(0xffffff, 1);
            graphic.fillRoundedRect(0, 0, 12, 12, 3);
            graphic.generateTexture(textureKey, 12, 12);
            graphic.destroy();
            this.controller.textureKey = textureKey;
        }

        scoreLabel(value, snapshot = this.controller.snapshot) {
            return `${Number(value || 0).toLocaleString()} ${snapshot?.scoreUnit || "pts"}`;
        }

        applySnapshot(snapshot, initial = false) {
            if (!snapshot) {
                return;
            }

            const previousPlayers = playerMap(this.previous);
            const currentIds = new Set((snapshot.players || []).map(player => player.playerId));
            const showRoundRanking = Boolean(snapshot.showRoundRanking && snapshot.results?.length);
            const pileUp = snapshot.gameKey === "pile-up-panic";
            const voiceChoon = snapshot.gameKey === "voicechoon";
            const pileFullCharacters = pileUp && [
                "Introduction", "ControllerReady", "RoundResult", "Standings",
                "FinalWinner", "WinnerCelebration", "Completed"
            ].includes(snapshot.phase);
            const characterMode = showRoundRanking || pileFullCharacters || voiceChoon ? "full" : "portrait";
            this.drawBackground(snapshot.gameKey, snapshot.phase);
            this.applyScreenChrome(snapshot);
            if (!initial && this.previous?.phase !== snapshot.phase && !showRoundRanking) {
                this.animatePhaseTransition(snapshot.phase);
            }

            for (const player of snapshot.players || []) {
                let avatar = this.avatars.get(player.playerId);
                if (!avatar) {
                    avatar = this.createAvatar(player);
                    this.avatars.set(player.playerId, avatar);
                    if (!initial) {
                        this.animateJoin(avatar);
                    }
                }

                const previousPlayer = previousPlayers.get(player.playerId);
                this.updateAvatar(avatar, player, characterMode);
                if (previousPlayer && previousPlayer.score !== player.score && !showRoundRanking) {
                    this.animateScore(avatar, player.score - previousPlayer.score);
                }
                if (previousPlayer && previousPlayer.status !== player.status) {
                    this.animatePresence(avatar, player.status);
                }
            }

            for (const [playerId, avatar] of this.avatars) {
                if (!currentIds.has(playerId)) {
                    this.animateLeave(playerId, avatar);
                }
            }

            if (pileUp) {
                this.stopRoundRanking();
                this.renderPodium({ ...snapshot, showRoundRanking: false });
                this.applyPresenter(null);
                this.applyTutorial(null);
                this.applyDrawing(null);
                this.applyPileUp(snapshot, initial);
                this.previous = cloneSnapshot(snapshot);
                return;
            }

            this.applyPileUp(null);
            if (voiceChoon) {
                this.stopRoundRanking();
                this.renderPodium({ ...snapshot, showRoundRanking: false });
                this.applyPresenter(null);
                this.applyTutorial(null);
                this.applyDrawing(null);
                this.applyVoiceChoon(snapshot, initial);
                this.previous = cloneSnapshot(snapshot);
                return;
            }
            this.applyVoiceChoon(null);

            if (showRoundRanking) {
                this.applyTutorial(null);
                this.applyDrawing(null);
                this.startRoundRanking(snapshot, initial);
                this.previous = cloneSnapshot(snapshot);
                return;
            }

            this.stopRoundRanking();
            const podiumChanged = this.renderPodium(snapshot);
            this.applyPresenter(snapshot.presenterMessage, snapshot.phase);
            this.applyTutorial(snapshot.tutorial);
            this.layoutAvatars(snapshot, initial, podiumChanged);
            this.applyDrawing(snapshot.drawing);
            this.previous = cloneSnapshot(snapshot);
        }

        applyDrawing(drawing) {
            const signature = JSON.stringify(drawing || null);
            if (signature === this.drawingSignature) {
                return;
            }
            this.drawingSignature = signature;
            this.drawingTimer?.remove(false);
            this.drawingTimer = null;
            this.drawingContainer?.destroy(true);
            this.drawingContainer = null;
            if (!drawing?.animations?.length) {
                return;
            }

            const expectedSignature = signature;
            const urls = [...new Set(drawing.animations.flatMap(animation => animation.frameUrls || []))];
            Promise.all(urls.map(url => this.loadDrawingTexture(url))).then(() => {
                if (this.drawingSignature !== expectedSignature || !this.scene?.isActive()) {
                    return;
                }
                this.startDrawingPlayback(drawing);
            }).catch(() => { });
        }

        applyPresenter(message, phase = null) {
            if (message === this.presenterMessage && phase === this.presenterPhase) return;
            this.presenterMessage = message;
            this.presenterPhase = phase;
            this.presenterRig?.stop();
            this.presenterRig = null;
            this.presenterContainer?.destroy(true);
            this.presenterContainer = null;
            if (!message) return;

            const hostCharacter = {
                bodyType: "Bean", presentation: "Man", skinTone: 3,
                hairColour: "Brown", shirtColour: "Navy", trouserColour: "Navy",
                trouserLength: "Long", shoeColour: "Brown", hairStyle: 5,
                eyeColour: "Blue", eyeSize: "Large", faceShape: "Round",
                noseShape: 1, browShape: 1, shoeStyle: 1, shirtStyle: 5,
                trouserStyle: 1, bodySize: "Regular"
            };
            const host = this.createAvatar({ displayName: "", score: 0 });
            this.drawCharacter(host, hostCharacter, "full");
            this.presenterRig = host.rig;
            host.name.setVisible(false);
            host.score.setVisible(false);
            host.presence.setVisible(false);
            host.activity.setVisible(false);
            const isBriefing = ["Briefing", "ShowdownBriefing"].includes(phase);
            host.container
                .setPosition(isBriefing ? -425 : -430, isBriefing ? 210 : 205)
                .setScale(isBriefing ? 1.42 : .68);

            const bubble = this.add.rectangle(isBriefing ? 150 : 115, -15,
                isBriefing ? 700 : 760, 235, 0xffffff, .97)
                .setStrokeStyle(8, 0x24123f, 1);
            const speech = this.add.text(isBriefing ? 150 : 115, -15, message, {
                color: "#24123f", fontFamily: displayFont, fontSize: "29px", fontStyle: "bold",
                align: "center", wordWrap: { width: isBriefing ? 620 : 680 }
            }).setOrigin(.5);
            this.presenterContainer = this.add.container(width / 2, isBriefing ? 205 : 260,
                [host.container, bubble, speech]).setDepth(70);
            if (!this.controller.reducedMotion) {
                this.presenterContainer.x = -500;
                this.tweens.add({
                    targets: this.presenterContainer, x: width / 2,
                    duration: 700, ease: "Back.easeOut"
                });
                host.rig.play(isBriefing ? "talk" : "idle");
                if (isBriefing) {
                    this.tweens.add({
                        targets: [bubble, speech], scale: { from: .985, to: 1.015 },
                        duration: 1150, yoyo: true, repeat: -1, ease: "Sine.easeInOut"
                    });
                }
            }
        }

        applyTutorial(tutorial) {
            const signature = JSON.stringify(tutorial || null);
            if (signature === this.tutorialSignature) return;
            this.tutorialSignature = signature;
            this.tutorialContainer?.destroy(true);
            this.tutorialContainer = null;
            if (!tutorial) return;

            const items = [];
            const panel = this.add.rectangle(0, 0, 1080, 250, 0x071a2e, .9)
                .setStrokeStyle(5, 0x67e8f9, 1);
            const title = this.add.text(0, -98, tutorial.title, {
                color: "#fde68a", fontFamily: displayFont, fontSize: "27px", fontStyle: "bold"
            }).setOrigin(.5);
            items.push(panel, title);

            const frames = Math.max(1, tutorial.frameCount || 1);
            const frameWidth = Math.min(115, 650 / frames);
            const startX = -(frames - 1) * (frameWidth + 14) / 2;
            for (let index = 0; index < frames; index++) {
                const x = startX + index * (frameWidth + 14);
                const card = this.add.rectangle(x, -35, frameWidth, 74, 0xffffff, .96)
                    .setStrokeStyle(4, index === 0 ? 0xfacc15 : 0xa78bfa, 1);
                const label = this.add.text(x, -35, `${index + 1}`, {
                    color: "#24123f", fontFamily: displayFont, fontSize: "27px", fontStyle: "bold"
                }).setOrigin(.5);
                items.push(card, label);
                if (index < frames - 1) {
                    items.push(this.add.text(x + frameWidth / 2 + 7, -35, "→", {
                        color: "#67e8f9", fontFamily: displayFont, fontSize: "24px", fontStyle: "bold"
                    }).setOrigin(.5));
                }
            }

            const tools = ["✏ DRAW", "◉ ONION", "↶ UNDO", "⌫ ERASE", "✓ SEND"];
            tools.forEach((tool, index) => {
                const x = (index - 2) * 190;
                const chip = this.add.text(x, 43, tool, {
                    color: "#ffffff", backgroundColor: index === 4 ? "#7c3aed" : "#164e63",
                    padding: { x: 14, y: 9 }, fontFamily: displayFont,
                    fontSize: "19px", fontStyle: "bold"
                }).setOrigin(.5);
                items.push(chip);
            });
            const hint = this.add.text(0, 96, tutorial.steps?.join("  •  ") || "", {
                color: "#dbeafe", fontFamily: bodyFont, fontSize: "16px",
                align: "center", wordWrap: { width: 1000 }
            }).setOrigin(.5);
            items.push(hint);

            this.tutorialContainer = this.add.container(width / 2, 555, items).setDepth(65);
            if (!this.controller.reducedMotion) {
                this.tutorialContainer.setAlpha(0).setScale(.92);
                this.tweens.add({
                    targets: this.tutorialContainer, alpha: 1, scale: 1,
                    duration: 600, delay: 420, ease: "Back.easeOut"
                });
                const firstCard = items[2];
                this.tweens.add({
                    targets: firstCard, scale: 1.08, duration: 450,
                    yoyo: true, repeat: -1, ease: "Sine.easeInOut"
                });
            }
        }

        loadDrawingTexture(url) {
            const key = `drawing-${url.split("/").pop()}`;
            if (this.textures.exists(key)) {
                return Promise.resolve(key);
            }
            return new Promise((resolve, reject) => {
                const image = new Image();
                image.decoding = "async";
                image.onload = () => {
                    if (!this.textures.exists(key)) {
                        this.textures.addImage(key, image);
                    }
                    resolve(key);
                };
                image.onerror = reject;
                image.src = url;
            });
        }

        startDrawingPlayback(drawing) {
            if (drawing.mode === "ShowdownReveal") {
                this.startShowdownReveal(drawing);
                return;
            }
            if (drawing.mode === "ShowdownPlayback") {
                this.startShowdownGallery(drawing);
                return;
            }
            let animationIndex = 0;
            let frameIndex = 0;
            let completedLoops = 0;
            const hasSideCards = ["Choosing", "Results"].includes(this.controller.snapshot?.phase);
            const targetScale = hasSideCards ? .84 : .78;
            const targetX = hasSideCards ? 285 : width / 2;
            const targetY = hasSideCards ? 335 : 310;
            const panelWidth = hasSideCards ? 500 : 530;
            const panelHeight = hasSideCards ? 440 : 430;
            const shadow = this.add.rectangle(16, 20, panelWidth, panelHeight, 0x090516, .42);
            const panel = this.add.rectangle(0, 0, panelWidth, panelHeight, 0xfffbeb, 0.99)
                .setStrokeStyle(9, 0x24123f, 1);
            const inner = this.add.rectangle(0, -22, hasSideCards ? 390 : 370,
                hasSideCards ? 370 : 370, 0xffffff, 1)
                .setStrokeStyle(3, 0xa78bfa, .8);
            const tapeY = -panelHeight / 2 + 13;
            const tapeLeftShadow = this.add.rectangle(-178, tapeY + 5, 102, 32, 0x090516, .18).setAngle(-8);
            const tapeRightShadow = this.add.rectangle(178, tapeY + 5, 102, 32, 0x090516, .18).setAngle(8);
            const tapeLeft = this.add.rectangle(-178, tapeY, 102, 32, 0xffe58f, .84)
                .setStrokeStyle(2, 0xffffff, .32).setAngle(-8);
            const tapeRight = this.add.rectangle(178, tapeY, 102, 32, 0x8cecff, .78)
                .setStrokeStyle(2, 0xffffff, .28).setAngle(8);
            const frame = this.add.image(0, -20, `drawing-${drawing.animations[0].frameUrls[0].split("/").pop()}`)
                .setDisplaySize(hasSideCards ? 360 : 350, hasSideCards ? 360 : 350);
            const caption = this.add.text(0, hasSideCards ? 185 : 172, "", {
                color: "#24123f",
                fontFamily: displayFont,
                fontSize: hasSideCards ? "23px" : "25px",
                fontStyle: "bold",
                align: "center",
                wordWrap: { width: panelWidth - 46 }
            }).setOrigin(0.5);
            const frameDots = Array.from({ length: drawing.animations[0].frameUrls.length }, (_, index) =>
                this.add.circle((index - (drawing.animations[0].frameUrls.length - 1) / 2) * 18,
                    panelHeight / 2 - 12, 5,
                    index === 0 ? 0xdb2777 : 0xc4b5fd, 1));
            this.drawingContainer = this.add.container(targetX, targetY,
                [shadow, panel, inner, tapeLeftShadow, tapeRightShadow,
                    tapeLeft, tapeRight, frame, caption, ...frameDots])
                .setDepth(12).setScale(targetScale);
            if (!this.controller.reducedMotion) {
                this.drawingContainer.setScale(targetScale * .82).setAlpha(0).setAngle(-2);
                this.tweens.add({
                    targets: this.drawingContainer, scale: targetScale, alpha: 1, angle: 0,
                    duration: 520, ease: "Back.easeOut"
                });
            }

            const show = () => {
                const animation = drawing.animations[animationIndex];
                const url = animation.frameUrls[frameIndex];
                frame.setTexture(`drawing-${url.split("/").pop()}`);
                frameDots.forEach((dot, index) => dot.setFillStyle(index === frameIndex ? 0xdb2777 : 0xc4b5fd));
                const reveal = drawing.mode === "Reveal" && animation.creatorName
                    ? `${animation.prompt}\n${animation.creatorName} — ${animation.votes} vote(s)`
                    : animation.prompt;
                caption.setText(reveal);
                frameIndex += 1;
                if (frameIndex >= animation.frameUrls.length) {
                    frameIndex = 0;
                    completedLoops += 1;
                    if (completedLoops >= Math.max(1, drawing.loopsPerAnimation || 1)) {
                        completedLoops = 0;
                        animationIndex = (animationIndex + 1) % drawing.animations.length;
                    }
                }
            };
            show();
            if (!this.controller.reducedMotion) {
                this.drawingTimer = this.time.addEvent({
                    delay: Math.max(100, drawing.frameDurationMilliseconds || 300),
                    loop: true,
                    callback: show
                });
            }
        }

        showdownGrid(animations, availableHeight = 350) {
            const columns = Math.min(3, animations.length);
            const rows = Math.ceil(animations.length / columns);
            const gapX = 18;
            const gapY = 16;
            const cardWidth = Math.min(330, Math.floor((1120 - gapX * (columns - 1)) / columns));
            const cardHeight = Math.min(rows === 1 ? 310 : 167,
                Math.floor((availableHeight - gapY * (rows - 1)) / rows));
            return { columns, rows, gapX, gapY, cardWidth, cardHeight };
        }

        startShowdownGallery(drawing) {
            const animations = drawing.animations || [];
            if (animations.length === 0) return;
            const grid = this.showdownGrid(animations);
            const frameSize = Math.max(82, Math.min(grid.cardWidth - 22, grid.cardHeight - 50));
            const items = [];
            const cards = [];
            animations.forEach((animation, index) => {
                const row = Math.floor(index / grid.columns);
                const itemsInRow = Math.min(grid.columns, animations.length - row * grid.columns);
                const column = index % grid.columns;
                const x = (column - (itemsInRow - 1) / 2) * (grid.cardWidth + grid.gapX);
                const y = row * (grid.cardHeight + grid.gapY);
                const shadow = this.add.rectangle(x + 7, y + 9, grid.cardWidth, grid.cardHeight, 0x090516, .38);
                const panel = this.add.rectangle(x, y, grid.cardWidth, grid.cardHeight, 0xfffbeb, .99)
                    .setStrokeStyle(5, 0x24123f, 1);
                const frame = this.add.image(x, y - 13,
                    `drawing-${animation.frameUrls[0].split("/").pop()}`)
                    .setDisplaySize(frameSize, frameSize);
                const label = this.add.text(x, y + grid.cardHeight / 2 - 17, animation.prompt, {
                    color: "#ffffff", backgroundColor: "#312e81", padding: { x: 11, y: 5 },
                    fontFamily: displayFont, fontSize: grid.rows > 1 ? "17px" : "21px", fontStyle: "bold"
                }).setOrigin(.5).setAngle(index % 2 === 0 ? -1 : 1);
                items.push(shadow, panel, frame, label);
                cards.push({ animation, frame, frameIndex: 0 });
            });
            this.drawingContainer = this.add.container(width / 2, 138 + grid.cardHeight / 2,
                items).setDepth(12);
            if (!this.controller.reducedMotion) {
                this.drawingContainer.setAlpha(0).setScale(.94);
                this.tweens.add({
                    targets: this.drawingContainer, alpha: 1, scale: 1,
                    duration: 420, ease: "Back.easeOut"
                });
            }
            const show = () => cards.forEach(card => {
                const url = card.animation.frameUrls[card.frameIndex];
                card.frame.setTexture(`drawing-${url.split("/").pop()}`);
                card.frameIndex = (card.frameIndex + 1) % card.animation.frameUrls.length;
            });
            show();
            if (!this.controller.reducedMotion) {
                this.drawingTimer = this.time.addEvent({
                    delay: Math.max(100, drawing.frameDurationMilliseconds || 300),
                    loop: true,
                    callback: show
                });
            }
        }

        applyPileUp(snapshot, initial = false) {
            const signature = snapshot ? JSON.stringify({
                phase: snapshot.phase,
                phaseEndsAtUtc: snapshot.phaseEndsAtUtc,
                phaseMessage: snapshot.phaseMessage,
                gameState: snapshot.gameState
            }) : null;
            if (signature === this.pileSignature) return;

            const previousPhase = this.previous?.phase;
            this.pileSignature = signature;
            this.pileDeadlineTimer?.remove(false);
            this.pileDeadlineTimer = null;
            this.pileCountdownTimer?.remove(false);
            this.pileCountdownTimer = null;
            this.pileContainer?.destroy(true);
            this.pileContainer = null;

            if (!snapshot) {
                this.pileEffectSignature = null;
                this.restorePileAvatars();
                return;
            }

            const state = snapshot.gameState;
            const match = field(state, "match", {});
            const arenas = field(match, "arenas", []);
            const items = [];
            for (const avatar of this.avatars.values()) {
                this.tweens.killTweensOf(avatar.container);
                avatar.container.setDepth(46);
                avatar.card.setVisible(false);
                avatar.cardShadow.setVisible(false);
                avatar.shadow.setVisible(false);
                avatar.presence.setVisible(false);
                avatar.name.setVisible(false);
                avatar.score.setVisible(false);
                avatar.wins.setVisible(false);
                avatar.activity.setVisible(false);
                avatar.remove.setVisible(false);
            }

            this.addPileHeader(snapshot, state, items);
            if (["Introduction", "ControllerReady"].includes(snapshot.phase)) {
                this.addPileIntro(snapshot, state, arenas, items);
            } else if (["ArenaReveal", "Countdown", "Playing"].includes(snapshot.phase)) {
                this.addPileArenas(snapshot, state, arenas, items);
            } else {
                this.addPileStandings(snapshot, state, arenas, items);
            }

            this.pileContainer = this.add.container(0, 0, items).setDepth(42);
            if (!this.controller.reducedMotion && (initial || previousPhase !== snapshot.phase)) {
                this.pileContainer.setAlpha(0);
                this.tweens.add({
                    targets: this.pileContainer,
                    alpha: 1,
                    duration: 320,
                    ease: "Cubic.easeOut"
                });
            }
        }

        restorePileAvatars() {
            const snapshot = this.controller.snapshot;
            for (const player of snapshot?.players || []) {
                const avatar = this.avatars.get(player.playerId);
                if (!avatar) continue;
                const portrait = avatar.mode === "portrait";
                avatar.container.setVisible(true).setDepth(20);
                avatar.character.setScale(portrait ? .4 : .31)
                    .setPosition(0, portrait ? -54 : -160);
                avatar.card.setVisible(portrait);
                avatar.cardShadow.setVisible(portrait);
                avatar.shadow.setVisible(!portrait);
                avatar.presence.setVisible(player.status === "Disconnected");
                avatar.name.setVisible(true);
                avatar.score.setVisible(true);
                avatar.wins.setVisible(snapshot.mode === "Lobby");
                avatar.activity.setVisible(player.activity === "Thinking");
                avatar.remove.setVisible(this.controller.canManagePlayers && snapshot.mode === "Lobby");
                avatar.pileAction = null;
            }
        }

        applyVoiceChoon(snapshot, initial = false) {
            const signature = snapshot ? JSON.stringify({
                phase: snapshot.phase,
                phaseEndsAtUtc: snapshot.phaseEndsAtUtc,
                phaseMessage: snapshot.phaseMessage,
                entries: snapshot.entries,
                results: snapshot.results,
                gameState: snapshot.gameState
            }) : null;
            if (signature === this.voiceSignature) return;
            this.voiceSignature = signature;
            this.voiceTimer?.remove(false);
            this.voiceTimer = null;
            this.voiceContainer?.destroy(true);
            this.voiceContainer = null;
            if (this.voiceFractalTextureKey && this.textures.exists(this.voiceFractalTextureKey)) {
                this.textures.remove(this.voiceFractalTextureKey);
            }
            this.voiceFractalTextureKey = null;
            if (!snapshot) {
                this.restorePileAvatars();
                return;
            }

            const state = snapshot.gameState || {};
            const players = snapshot.players || [];
            const entries = snapshot.entries || [];
            const items = [];
            const playing = snapshot.phase === "Playing";
            const showingResults = snapshot.phase === "Results";
            const rankedResults = [...(snapshot.results || [])].sort((a, b) =>
                a.rank - b.rank || b.pointsAwarded - a.pointsAwarded ||
                normalizedId(a.playerId).localeCompare(normalizedId(b.playerId)));
            const resultByPlayer = new Map(rankedResults.map(result => [normalizedId(result.playerId), result]));
            const podiumSlotByPlayer = new Map(rankedResults.map((result, index) =>
                [normalizedId(result.playerId), index + 1]));
            const topRank = Math.min(...rankedResults.map(result => Number(result.rank || 0)));
            const lastRank = Math.max(0, ...rankedResults.map(result => Number(result.rank || 0)));
            const hasLosingRank = lastRank > topRank;
            if (this.voiceStreakGame !== snapshot.gameInstanceId) {
                this.voiceStreakGame = snapshot.gameInstanceId;
                this.voiceLastStreak = 0;
            }
            const dances = ["bowLegged", "armFlap", "fistPump", "discoPoint", "rubberRobot"];
            const playback = field(state, "playback", []);
            const attackTimes = playback.map(note => Number(field(note, "startTimeSeconds", 0)))
                .filter(Number.isFinite).sort((a, b) => a - b);
            const intervals = attackTimes.slice(1).map((time, index) => time - attackTimes[index])
                .filter(value => value >= .22 && value <= .9).sort((a, b) => a - b);
            const beatSeconds = intervals.length ? intervals[Math.floor(intervals.length / 2)] : .48;
            const beatMs = Math.round(beatSeconds * 1000);
            const visualSeed = stableVisualSeed(snapshot.gameInstanceId);
            const fractalStyle = (visualSeed >>> 3) % 2 === 0 ? "branches" : "mandelbrot";
            const psychedelic = this.add.graphics().setDepth(0);
            const fractal = this.add.graphics().setDepth(1).setAlpha(0);
            let mandelbrotTexture = null;
            let mandelbrot = null;
            if (playing && fractalStyle === "mandelbrot") {
                this.voiceFractalTextureKey = `voice-mandelbrot-${snapshot.gameInstanceId}`;
                mandelbrotTexture = this.textures.createCanvas(this.voiceFractalTextureKey, 192, 108);
                mandelbrot = this.add.image(width / 2, height / 2, this.voiceFractalTextureKey)
                    .setDisplaySize(width, height).setDepth(1).setAlpha(0);
            }
            const beams = this.add.graphics().setBlendMode(Phaser.BlendModes.ADD).setDepth(2);
            const stageShade = this.add.graphics().setDepth(3);
            stageShade.fillStyle(0x03030d, .18);
            stageShade.fillRect(0, 0, width, height);
            stageShade.fillStyle(0x090516, .55);
            stageShade.fillRect(0, 655, width, 65);
            items.push(psychedelic, fractal);
            if (mandelbrot) items.push(mandelbrot);
            items.push(beams, stageShade);
            if (!showingResults) items.push(
                this.add.text(38, 28, playing ? "VOICECHOON · LIVE" : "VOICECHOON", {
                    color: "#ffffff", fontFamily: displayFont, fontSize: "27px", fontStyle: "bold",
                    stroke: "#130828", strokeThickness: 7, letterSpacing: 2
                }),
                this.add.text(1240, 33, snapshot.phaseMessage || snapshot.phase, {
                    color: "#fff36e", fontFamily: displayFont, fontSize: "18px", fontStyle: "bold",
                    stroke: "#130828", strokeThickness: 5
                }).setOrigin(1, 0));

            if (showingResults) {
                drawVoiceChoonMarbling(psychedelic, 18, .35, 1, visualSeed);
                const winner = rankedResults.length
                    ? players.find(player => normalizedId(player.playerId) === normalizedId(rankedResults[0].playerId))
                    : null;
                const total = Number(field(state, "bandScore", 0));
                items.push(
                    this.add.text(width / 2, 75, `${total.toLocaleString()} TOTAL BAND POINTS`, {
                        color: "#fff36e", fontFamily: displayFont, fontSize: "42px", fontStyle: "bold",
                        stroke: "#3b0764", strokeThickness: 9
                    }).setOrigin(.5).setDepth(60),
                    this.add.text(width / 2, 125,
                        winner ? `${winner.displayName.toUpperCase()} TOP SCORED!` : "WHAT A PERFORMANCE!", {
                            color: "#ffffff", fontFamily: displayFont, fontSize: "25px", fontStyle: "bold",
                            stroke: "#3b0764", strokeThickness: 6
                        }).setOrigin(.5).setDepth(60));
                const podium = this.add.graphics().setDepth(25);
                items.push(podium);
                const podiumSteps = [
                    { x: 640, y: 560, w: 270, h: 132, colour: 0xfacc15, rank: 1 },
                    { x: 350, y: 598, w: 235, h: 94, colour: 0x94a3b8, rank: 2 },
                    { x: 930, y: 620, w: 235, h: 72, colour: 0xd97706, rank: 3 }
                ];
                podiumSteps.slice(0, Math.min(3, rankedResults.length)).forEach((step, index) => {
                    podium.fillStyle(0x130925, .94).fillRoundedRect(
                        step.x - step.w / 2, step.y, step.w, step.h, 18);
                    podium.lineStyle(5, step.colour, .95).strokeRoundedRect(
                        step.x - step.w / 2, step.y, step.w, step.h, 18);
                    items.push(this.add.text(step.x, step.y + 34, `#${rankedResults[index].rank}`, {
                        color: `#${step.colour.toString(16).padStart(6, "0")}`,
                        fontFamily: displayFont, fontSize: "31px", fontStyle: "bold"
                    }).setOrigin(.5).setDepth(55));
                });
            }

            players.forEach((player, index) => {
                const avatar = this.avatars.get(player.playerId);
                if (!avatar) return;
                this.tweens.killTweensOf(avatar.container);
                const result = resultByPlayer.get(normalizedId(player.playerId));
                const rank = Number(result?.rank || 0);
                const podiumSlot = podiumSlotByPlayer.get(normalizedId(player.playerId)) || 0;
                const columns = Math.min(4, players.length);
                const rows = Math.ceil(players.length / columns);
                const row = Math.floor(index / columns);
                const rowCount = Math.min(columns, players.length - row * columns);
                const column = index % columns;
                const spacing = Math.min(300, 1140 / Math.max(1, rowCount));
                const podiumPosition = podiumSlot === 1 ? { x: 640, y: 555, scale: .76 }
                    : podiumSlot === 2 ? { x: 350, y: 595, scale: .62 }
                        : podiumSlot === 3 ? { x: 930, y: 617, scale: .58 } : null;
                const remainingIndex = Math.max(0, podiumSlot - 4);
                const remainingCount = Math.max(1, players.length - 3);
                const x = showingResults ? podiumPosition?.x
                    ?? width / 2 + (remainingIndex - (remainingCount - 1) / 2) * Math.min(190, 900 / remainingCount)
                    : width / 2 + (column - (rowCount - 1) / 2) * spacing;
                const y = showingResults ? podiumPosition?.y ?? 670 : rows === 1 ? 575 : 335 + row * 295;
                const scale = showingResults ? podiumPosition?.scale ?? .38
                    : rows === 1 ? .94 : players.length <= 6 ? .67 : .58;
                avatar.container.setVisible(true).setDepth(48 + row).setPosition(x, y).setScale(scale);
                avatar.character
                    .setScale(showingResults ? (podiumSlot === 1 ? .62 : .54) : rows === 1 ? .72 : .56)
                    .setPosition(0, showingResults ? (podiumSlot <= 3 ? -330 : -295) : rows === 1 ? -365 : -325);
                avatar.card.setVisible(false);
                avatar.cardShadow.setVisible(false);
                avatar.shadow.setVisible(true);
                avatar.presence.setVisible(player.status === "Disconnected");
                avatar.name.setVisible(!showingResults).setY(38);
                avatar.score.setVisible(false).setY(70);
                avatar.wins.setVisible(false);
                avatar.activity.setVisible(false);
                avatar.remove.setVisible(false);
                if (this.controller.reducedMotion) avatar.rig?.stop();
                else avatar.rig?.play(playing ? dances[index % dances.length]
                    : showingResults && rank === topRank ? "celebrate"
                        : showingResults && rank === lastRank && hasLosingRank ? "cry"
                            : showingResults ? dances[index % dances.length] : "idle", { beatMs });
                const roleLabel = players.length === 1 ? "ONE-HUMAN ORCHESTRA"
                    : entries[index]?.value || "Band member";
                const resultLabel = showingResults
                    ? `${player.displayName.toUpperCase()}\n#${rank} · ${Number(result?.pointsAwarded || 0).toLocaleString()} PTS`
                    : roleLabel;
                const resultLabelY = podiumSlot === 1 ? 205
                    : podiumSlot === 2 ? 300 : podiumSlot === 3 ? 335 : 525;
                items.push(this.add.text(x, showingResults ? resultLabelY
                    : rows === 1 ? 116 : y - 205, resultLabel, {
                        color: laneColoursForVoice(index), backgroundColor: "#160a31cc",
                        padding: { x: 12, y: 6 }, fontFamily: displayFont,
                        fontSize: rows === 1 ? "19px" : "14px", fontStyle: "bold", align: "center",
                        wordWrap: { width: rows === 1 ? 255 : 205 }
                    }).setOrigin(.5).setDepth(54));
            });

            if (snapshot.phase === "Countdown" && snapshot.phaseEndsAtUtc) {
                const countdown = this.add.text(width / 2, height / 2 - 45, "", {
                    color: "#c8ff36", fontFamily: displayFont, fontSize: "148px", fontStyle: "bold",
                    stroke: "#160a31", strokeThickness: 18, align: "center",
                    shadow: { offsetX: 0, offsetY: 12, color: "#ff3fa4", blur: 22, fill: true }
                }).setOrigin(.5).setDepth(95);
                const countdownLabel = this.add.text(width / 2, height / 2 + 78, "GET READY", {
                    color: "#ffffff", fontFamily: displayFont, fontSize: "34px", fontStyle: "bold",
                    stroke: "#160a31", strokeThickness: 8, letterSpacing: 5
                }).setOrigin(.5).setDepth(95);
                let previousSecond = null;
                const updateCountdown = () => {
                    const remaining = Math.max(0, Date.parse(snapshot.phaseEndsAtUtc) - Date.now());
                    const second = Math.max(1, Math.ceil(remaining / 1000));
                    if (second !== previousSecond) {
                        previousSecond = second;
                        countdown.setText(String(second)).setAlpha(1).setScale(.48)
                            .setAngle(second % 2 === 0 ? -3 : 3);
                        void this.controller.voiceAudio?.playCountdownBlip();
                        this.tweens.killTweensOf(countdown);
                        if (this.controller.reducedMotion) countdown.setScale(1).setAngle(0);
                        else this.tweens.add({
                            targets: countdown,
                            scale: 2.05,
                            alpha: 0,
                            angle: 0,
                            duration: 880,
                            ease: "Cubic.easeOut"
                        });
                    }
                };
                updateCountdown();
                this.voiceTimer = this.time.addEvent({ delay: 100, loop: true, callback: updateCountdown });
                items.push(countdown, countdownLabel);
            }

            if (playing) {
                const sectionText = this.add.text(35, 674, "INTRO", {
                    color: "#ffffff", fontFamily: displayFont, fontSize: "22px", fontStyle: "bold"
                }).setDepth(60);
                const comboText = this.add.text(1245, 674, "", {
                    color: "#fff36e", fontFamily: displayFont, fontSize: "21px", fontStyle: "bold"
                }).setOrigin(1, 0).setDepth(60);
                const progress = this.add.rectangle(205, 689, 0, 8, 0x67e8f9, 1).setOrigin(0, .5).setDepth(60);
                const performerData = field(state, "performers", []);
                const judged = new Set(performerData.flatMap(item => field(item, "judgedNoteIds", []))
                    .map(normalizedId));
                const missedByPlayer = new Map();
                const started = Date.parse(field(state, "songStartsAtUtc", new Date().toISOString()));
                const initialElapsed = Math.max(0, (Date.now() - started) / 1000);
                performerData.forEach(performer => {
                    const playerId = normalizedId(field(performer, "playerId", ""));
                    const missed = new Set(field(performer, "notes", [])
                        .filter(note => Number(field(note, "startTimeSeconds", 0)) + .65 < initialElapsed
                            && !judged.has(normalizedId(field(note, "noteId", ""))))
                        .map(note => normalizedId(field(note, "noteId", ""))));
                    missedByPlayer.set(playerId, missed);
                });
                items.push(
                    sectionText,
                    comboText,
                    this.add.rectangle(720, 689, 1030, 8, 0xffffff, .18).setDepth(59),
                    progress);
                const showStreak = streak => {
                    if (streak < 10 || streak === this.voiceLastStreak || streak % 10 !== 0) return;
                    this.voiceLastStreak = streak;
                    const phrases = ["UNREAL!", "HUMAN JUKEBOX!", "FACE-MELTING!", "ABSURD STREAK!", "VOICE LEGENDS!"];
                    const banner = this.add.text(width / 2, height / 2 - 30,
                        `${phrases[(streak / 10 - 1) % phrases.length]}  ${streak} HITS`, {
                            color: "#ffffff", fontFamily: displayFont, fontSize: "58px", fontStyle: "bold",
                            stroke: "#ec4899", strokeThickness: 14, align: "center"
                        }).setOrigin(.5).setDepth(90).setAngle(-4).setScale(.2);
                    this.tweens.add({ targets: banner, scale: 1, angle: 2, duration: 260,
                        ease: "Back.easeOut", yoyo: true, hold: 850,
                        onComplete: () => banner.destroy() });
                };
                let lastMandelbrotFrame = -1;
                const update = () => {
                    const duration = Number(field(state, "songDurationSeconds", 1));
                    const elapsed = Math.max(0, (Date.now() - started) / 1000);
                    progress.width = 1030 * Math.max(0, Math.min(1, elapsed / duration));
                    const beatPhase = (elapsed % beatSeconds) / beatSeconds;
                    const pulse = 1 - Math.min(1, beatPhase * 2.4);
                    const visualElapsed = this.controller.reducedMotion ? 0 : elapsed;
                    const visualPulse = this.controller.reducedMotion ? 0 : pulse;
                    const songProgress = elapsed / duration;
                    const transitionSeconds = 17 + (visualSeed % 11);
                    const transitionPhase = ((visualElapsed + (visualSeed % 19)) % transitionSeconds)
                        / transitionSeconds;
                    const transitionWave = (Math.sin(transitionPhase * Math.PI * 2) + 1) / 2;
                    const fractalMix = transitionWave * transitionWave * (3 - 2 * transitionWave);
                    drawVoiceChoonMarbling(
                        psychedelic, visualElapsed, visualPulse, songProgress, visualSeed);
                    if (fractalStyle === "mandelbrot" && mandelbrotTexture) {
                        const mandelbrotFrame = Math.floor(visualElapsed * 6);
                        if (mandelbrotFrame !== lastMandelbrotFrame) {
                            lastMandelbrotFrame = mandelbrotFrame;
                            drawVoiceChoonMandelbrot(
                                mandelbrotTexture, visualElapsed, visualPulse, songProgress, visualSeed);
                        }
                    } else {
                        drawVoiceChoonFractal(
                            fractal, visualElapsed, visualPulse, songProgress, visualSeed);
                    }
                    psychedelic.setAlpha(1 - fractalMix * .92);
                    fractal.setAlpha(fractalStyle === "branches" ? fractalMix : 0);
                    mandelbrot?.setAlpha(fractalMix);
                    const spotlightColours = [0xfff1a8, 0xf9a8d4, 0x93c5fd, 0xffffff];
                    beams.clear();
                    [0, 1, 2, 3].forEach(index => {
                        const anchor = 100 + index * 360;
                        const sweep = Math.sin(visualElapsed * (.62 + index * .08) + index) * 430;
                        beams.fillStyle(spotlightColours[index], .1 + visualPulse * .07);
                        beams.fillTriangle(anchor, -20, anchor - 42, -20, width / 2 + sweep, 710);
                    });
                    const sections = field(state, "sections", []);
                    const current = [...sections].reverse().find(item =>
                        Number(field(item, "startTimeSeconds", 0)) <= elapsed);
                    sectionText.setText(field(current, "name", "INTRO"));
                    const completed = performerData.flatMap(performer => field(performer, "notes", []))
                        .filter(note => Number(field(note, "startTimeSeconds", 0)) + .65 < elapsed)
                        .sort((a, b) => Number(field(a, "startTimeSeconds", 0)) - Number(field(b, "startTimeSeconds", 0)));
                    let streak = 0;
                    for (let index = completed.length - 1; index >= 0; index--) {
                        if (!judged.has(normalizedId(field(completed[index], "noteId", "")))) break;
                        streak++;
                    }
                    comboText.setText(streak ? `${streak}× HIT STREAK` : "KEEP THE BEAT");
                    showStreak(streak);
                    performerData.forEach(performer => {
                        const playerId = normalizedId(field(performer, "playerId", ""));
                        const missed = missedByPlayer.get(playerId) || new Set();
                        const newlyMissed = field(performer, "notes", []).find(note => {
                            const id = normalizedId(field(note, "noteId", ""));
                            return Number(field(note, "startTimeSeconds", 0)) + .65 < elapsed
                                && !judged.has(id) && !missed.has(id);
                        });
                        if (!newlyMissed) return;
                        missed.add(normalizedId(field(newlyMissed, "noteId", "")));
                        const playerIndex = players.findIndex(player => normalizedId(player.playerId) === playerId);
                        const avatar = playerIndex < 0 ? null : this.avatars.get(players[playerIndex].playerId);
                        if (!avatar || this.controller.reducedMotion) return;
                        avatar.rig?.play("dazed", {
                            onComplete: () => avatar.rig?.play(dances[playerIndex % dances.length], { beatMs })
                        });
                    });
                };
                update();
                this.voiceTimer = this.time.addEvent({ delay: 50, loop: true, callback: update });
            }

            this.voiceContainer = this.add.container(0, 0, items).setDepth(42);
            this.voiceContainer.setAlpha(1);
        }

        playPileAvatar(avatar, action) {
            if (!avatar) return;
            const requested = this.controller.reducedMotion ? "stopped" : action;
            if (avatar.pileAction === requested) return;
            avatar.pileAction = requested;
            if (requested === "stopped") avatar.rig?.stop();
            else avatar.rig?.play(requested);
        }

        pileValue(dictionary, playerId, fallback = 0) {
            if (!dictionary) return fallback;
            const target = normalizedId(playerId);
            const key = Object.keys(dictionary).find(candidate => normalizedId(candidate) === target);
            return key ? dictionary[key] : fallback;
        }

        pileMaterialColour(material) {
            return ({
                copper: 0xf28b39,
                aqua: 0x25d9dc,
                lemon: 0xffdf3d,
                violet: 0x9b6cff,
                coral: 0xff617d,
                mint: 0x58e3a5,
                sky: 0x5ba8ff,
                sand: 0xe8c785,
                junk: 0x6f7787
            })[String(material || "").toLowerCase()] || 0xb9c4d4;
        }

        pileAbilityLabel(ability) {
            const key = String(ability ?? "");
            return ({
                "0": "SEND JUNK",
                "1": "SCRAMBLE QUEUE",
                "2": "SHIELD",
                SendJunk: "SEND JUNK",
                ScrambleQueue: "SCRAMBLE QUEUE",
                Shield: "SHIELD"
            })[key] || key.replace(/([a-z])([A-Z])/g, "$1 $2").toUpperCase();
        }

        pileActiveCells(state, active) {
            if (!active) return [];
            const shapes = field(state, "clusterShapes", {});
            const shape = field(shapes, field(active, "clusterKey", ""), []);
            const turns = ((Number(field(active, "rotation", 0)) % 4) + 4) % 4;
            const transformed = shape.map(cell => {
                const x = Number(field(cell, "x", 0));
                const y = Number(field(cell, "y", 0));
                if (turns === 1) return { x: -y, y: x };
                if (turns === 2) return { x: -x, y: -y };
                if (turns === 3) return { x: y, y: -x };
                return { x, y };
            });
            const minimumX = Math.min(0, ...transformed.map(cell => cell.x));
            const minimumY = Math.min(0, ...transformed.map(cell => cell.y));
            const originX = Number(field(active, "x", 0));
            const originY = Number(field(active, "y", 0));
            return transformed.map(cell => ({
                x: cell.x - minimumX + originX,
                y: cell.y - minimumY + originY,
                material: field(active, "material", "sand")
            }));
        }

        addPileHeader(snapshot, state, items) {
            const round = Number(field(state, "roundNumber", 1));
            const topLabel = snapshot.phase === "WinnerCelebration"
                ? "SCRAPYARD CHAMPION"
                : `PILE-UP PANIC  ·  ROUND ${round}/3`;
            items.push(
                this.add.text(width / 2, 28, topLabel, {
                    color: "#ffe86a", fontFamily: displayFont, fontSize: "20px",
                    fontStyle: "bold", letterSpacing: 4,
                    stroke: "#080914", strokeThickness: 5
                }).setOrigin(.5, 0),
                this.add.text(width / 2, 56, snapshot.phaseMessage || "", {
                    color: "#ffffff", fontFamily: displayFont,
                    fontSize: snapshot.phase === "Countdown" ? "30px" : "25px",
                    fontStyle: "bold", stroke: "#080914", strokeThickness: 6,
                    align: "center", wordWrap: { width: 880 }
                }).setOrigin(.5, 0)
            );

            if (!snapshot.phaseEndsAtUtc) return;
            const timer = this.add.text(1198, 34, "", {
                color: "#ffe86a", backgroundColor: "#14162a",
                padding: { x: 13, y: 8 }, fontFamily: displayFont,
                fontSize: "24px", fontStyle: "bold", stroke: "#080914", strokeThickness: 3
            }).setOrigin(1, 0).setDepth(3);
            items.push(timer);
            const update = () => {
                const milliseconds = Math.max(0, Date.parse(snapshot.phaseEndsAtUtc) - Date.now());
                timer.setText(`${Math.ceil(milliseconds / 1000)}s`);
            };
            update();
            this.pileDeadlineTimer = this.time.addEvent({ delay: 250, loop: true, callback: update });
        }

        addPileIntro(snapshot, state, arenas, items) {
            const readyIds = new Set(field(state, "readyPlayerIds", []).map(normalizedId));
            const isReady = snapshot.phase === "ControllerReady";
            items.push(
                this.add.text(width / 2, 132,
                    isReady ? "CONTROLLERS ONLINE" : "WELCOME TO THE SCRAPYARD", {
                    color: "#ffffff", fontFamily: displayFont, fontSize: "48px", fontStyle: "bold",
                    stroke: "#ff4fa3", strokeThickness: 8
                }).setOrigin(.5),
                this.add.text(width / 2, 196,
                    isReady
                        ? "Open your controls, then ready up."
                        : "Build complete circuits. Charge chaos. Be the last pile standing.", {
                    color: "#cdefff", fontFamily: bodyFont, fontSize: "24px", fontStyle: "bold",
                    align: "center", wordWrap: { width: 960 }
                }).setOrigin(.5)
            );

            const count = Math.max(1, arenas.length);
            const spacing = Math.min(250, 1020 / count);
            arenas.forEach((arena, index) => {
                const playerId = normalizedId(field(arena, "playerId", ""));
                const player = (snapshot.players || []).find(candidate => normalizedId(candidate.playerId) === playerId);
                const avatar = player ? this.avatars.get(player.playerId) : null;
                const x = width / 2 + (index - (count - 1) / 2) * spacing;
                const ready = readyIds.has(playerId);
                if (avatar) {
                    avatar.container.setVisible(true).setPosition(x, 538).setScale(.68)
                        .setAlpha(field(arena, "isConnected", true) ? 1 : .38);
                    this.playPileAvatar(avatar, ready ? "celebrate" : "idle");
                }
                items.push(
                    this.add.ellipse(x, 552, 150, 30, 0x03050b, .55),
                    this.add.text(x, 600, field(arena, "displayName", player?.displayName || "Player"), {
                        color: "#ffffff", fontFamily: displayFont, fontSize: "23px", fontStyle: "bold",
                        stroke: "#080914", strokeThickness: 5
                    }).setOrigin(.5),
                    this.add.text(x, 634, isReady ? (ready ? "READY" : "WAITING…") : "SCRAP PILOT", {
                        color: ready ? "#58e3a5" : "#ffe86a", fontFamily: displayFont,
                        fontSize: "15px", fontStyle: "bold", letterSpacing: 2
                    }).setOrigin(.5)
                );
            });
        }

        addPileArenas(snapshot, state, arenas, items) {
            const count = Math.max(1, arenas.length);
            const gap = count === 4 ? 12 : 18;
            const availableWidth = 1190;
            const cardWidth = (availableWidth - gap * (count - 1)) / count;
            const cellSize = Math.min(count === 2 ? 28 : count === 3 ? 25 : 21,
                Math.floor((cardWidth - 24) / 9));
            const gridWidth = cellSize * 9;
            const gridHeight = cellSize * 17;
            const cardHeight = Math.min(555, gridHeight + 132);
            const cardTop = 112;
            const startX = (width - availableWidth) / 2;

            arenas.forEach((arena, index) => {
                const x = startX + cardWidth / 2 + index * (cardWidth + gap);
                const playerId = normalizedId(field(arena, "playerId", ""));
                const player = (snapshot.players || []).find(candidate => normalizedId(candidate.playerId) === playerId);
                const avatar = player ? this.avatars.get(player.playerId) : null;
                const overloaded = Boolean(field(arena, "isOverloaded", false));
                const connected = Boolean(field(arena, "isConnected", true));
                const shielded = Boolean(field(arena, "shielded", false));
                const previousArenas = field(field(this.previous?.gameState, "match", {}), "arenas", []);
                const previousArena = previousArenas.find(candidate =>
                    normalizedId(field(candidate, "playerId", "")) === playerId);
                const graphics = this.add.graphics();
                const cardLeft = x - cardWidth / 2;
                graphics.fillStyle(0x080914, .86);
                graphics.fillRoundedRect(cardLeft, cardTop, cardWidth, cardHeight, 18);
                graphics.lineStyle(4, overloaded ? 0xff617d : shielded ? 0x58e3a5 : 0x38d8ff, .9);
                graphics.strokeRoundedRect(cardLeft, cardTop, cardWidth, cardHeight, 18);

                const gridLeft = x - gridWidth / 2;
                const gridTop = cardTop + 68;
                graphics.fillStyle(0x02040a, .96);
                graphics.fillRect(gridLeft, gridTop, gridWidth, gridHeight);
                graphics.lineStyle(1, 0x9beaff, .1);
                for (let column = 0; column <= 9; column++) {
                    graphics.lineBetween(gridLeft + column * cellSize, gridTop,
                        gridLeft + column * cellSize, gridTop + gridHeight);
                }
                for (let row = 0; row <= 17; row++) {
                    graphics.lineBetween(gridLeft, gridTop + row * cellSize,
                        gridLeft + gridWidth, gridTop + row * cellSize);
                }

                const drawCell = (target, cell, isActive = false) => {
                    const cellX = Number(field(cell, "x", -1));
                    const cellY = Number(field(cell, "y", -1)) - 3;
                    if (cellX < 0 || cellX >= 9 || cellY < 0 || cellY >= 17) return;
                    const colour = this.pileMaterialColour(field(cell, "material", "junk"));
                    const px = gridLeft + cellX * cellSize + 2;
                    const py = gridTop + cellY * cellSize + 2;
                    if (isActive) {
                        target.lineStyle(2, 0xffffff, .72);
                        target.strokeRoundedRect(px - 1, py - 1, cellSize - 2, cellSize - 2,
                            Math.max(2, cellSize / 6));
                    }
                    target.fillStyle(colour, .96);
                    target.fillRoundedRect(px, py, cellSize - 4, cellSize - 4, Math.max(2, cellSize / 6));
                    target.fillStyle(0xffffff, isActive ? .34 : .2);
                    target.fillRect(px + 2, py + 2, cellSize - 8, 3);
                };
                field(arena, "grid", []).forEach(cell => drawCell(graphics, cell));

                const active = field(arena, "active", null);
                const activeGraphics = this.add.graphics();
                this.pileActiveCells(state, active).forEach(cell => drawCell(activeGraphics, cell, true));
                const previousActive = field(previousArena, "active", null);
                const canInterpolate = snapshot.phase === "Playing" && previousActive && active &&
                    field(previousActive, "clusterKey", "") === field(active, "clusterKey", "") &&
                    field(previousActive, "material", "") === field(active, "material", "") &&
                    Number(field(previousActive, "rotation", 0)) === Number(field(active, "rotation", 0));
                if (!this.controller.reducedMotion && canInterpolate) {
                    activeGraphics.setPosition(
                        (Number(field(previousActive, "x", 0)) - Number(field(active, "x", 0))) * cellSize,
                        (Number(field(previousActive, "y", 0)) - Number(field(active, "y", 0))) * cellSize);
                    this.tweens.add({
                        targets: activeGraphics,
                        x: 0,
                        y: 0,
                        duration: 60,
                        ease: "Cubic.easeOut"
                    });
                }
                items.push(graphics, activeGraphics);

                if (avatar) {
                    avatar.container.setVisible(true).setPosition(cardLeft + 34, cardTop + 39).setScale(.28)
                        .setAlpha(connected ? 1 : .35);
                    this.playPileAvatar(avatar, overloaded ? "cry" : "idle");
                }

                const upcoming = field(arena, "upcoming", []);
                items.push(
                    this.add.text(x - cardWidth / 2 + 62, cardTop + 19,
                        field(arena, "displayName", player?.displayName || "Player"), {
                        color: "#ffffff", fontFamily: displayFont,
                        fontSize: count === 4 ? "16px" : "20px", fontStyle: "bold",
                        stroke: "#080914", strokeThickness: 4
                    }).setOrigin(0, .5),
                    this.add.text(x + cardWidth / 2 - 12, cardTop + 19,
                        `${Number(field(arena, "views", 0)).toLocaleString()} views`, {
                        color: "#ffe86a", fontFamily: displayFont,
                        fontSize: count === 4 ? "13px" : "15px", fontStyle: "bold"
                    }).setOrigin(1, .5)
                );
                upcoming.slice(0, 2).forEach((scrap, queueIndex) => {
                    items.push(this.add.circle(
                        x + cardWidth / 2 - 15 - queueIndex * 20,
                        cardTop + 48, 7,
                        this.pileMaterialColour(field(scrap, "material", "sand")), 1)
                        .setStrokeStyle(2, 0xffffff, .45));
                });

                const ability = field(arena, "availableAbility", null);
                const charge = Number(field(arena, "chaosCharge", 0));
                const footerY = gridTop + gridHeight + 12;
                const chargeWidth = Math.max(50, cardWidth - 24);
                const chargeFill = Math.max(0, Math.min(1, ability !== null ? 1 : charge / 100));
                const chargeBackground = this.add.rectangle(x, footerY + 10, chargeWidth, 13, 0x24283b, 1)
                    .setStrokeStyle(2, 0x697386, .8);
                const chargeBar = this.add.rectangle(
                    x - chargeWidth / 2 + (chargeWidth * chargeFill) / 2,
                    footerY + 10, chargeWidth * chargeFill, 9,
                    ability !== null ? 0xff4fa3 : 0x38d8ff, 1);
                items.push(chargeBackground, chargeBar);
                const status = overloaded ? "⚠ OVERLOADED"
                    : !connected ? "OFFLINE"
                        : ability !== null ? `CHAOS READY · ${this.pileAbilityLabel(ability)}`
                            : shielded ? "SHIELD ACTIVE"
                                : field(arena, "queuedJunk", 0) > 0 ? `${field(arena, "queuedJunk", 0)} JUNK QUEUED`
                                    : `${field(arena, "circuitsCompleted", 0)} CIRCUITS · CHAOS ${charge}%`;
                items.push(this.add.text(x, footerY + 31, status, {
                    color: overloaded || !connected ? "#ff8ba0" : shielded ? "#6effbd" : "#cdefff",
                    fontFamily: displayFont, fontSize: count === 4 ? "11px" : "13px",
                    fontStyle: "bold", align: "center", wordWrap: { width: cardWidth - 18 }
                }).setOrigin(.5, 0));

                if (previousArena && Number(field(arena, "views", 0)) > Number(field(previousArena, "views", 0))) {
                    this.burst(x, gridTop + gridHeight * .55, 18);
                }
                if (previousArena && Number(field(arena, "circuitsCompleted", 0)) >
                    Number(field(previousArena, "circuitsCompleted", 0))) {
                    this.pileCircuitExplosion(x, gridTop + gridHeight * .55, gridWidth, items);
                }
                if (!this.controller.reducedMotion && previousArena && overloaded &&
                    !Boolean(field(previousArena, "isOverloaded", false))) {
                    this.cameras.main.shake(160, .004);
                }
            });

            if (snapshot.phase === "Countdown") {
                const countdown = this.add.text(width / 2, height / 2, "", {
                    color: "#ffffff", fontFamily: displayFont, fontSize: "150px", fontStyle: "bold",
                    stroke: "#ff4fa3", strokeThickness: 15
                }).setOrigin(.5).setDepth(10);
                const update = () => countdown.setText(String(Math.max(1,
                    Math.ceil((Date.parse(snapshot.phaseEndsAtUtc) - Date.now()) / 1000))));
                update();
                this.pileCountdownTimer = this.time.addEvent({ delay: 150, loop: true, callback: update });
                items.push(countdown);
            }
        }

        pileCircuitExplosion(x, y, gridWidth, items) {
            const flash = this.add.rectangle(x, y, gridWidth, 18, 0xfff36e, .9).setDepth(60);
            const ring = this.add.circle(x, y, 18, 0xffc51b, 0).setStrokeStyle(6, 0xfff36e, .95).setDepth(60);
            items.push(flash, ring);
            if (this.controller.reducedMotion) {
                flash.setAlpha(.35);
                ring.setRadius(70).setAlpha(0);
                return;
            }
            this.tweens.add({
                targets: flash, alpha: 0, scaleX: 1.08, duration: 360,
                ease: "Cubic.easeOut", onComplete: () => flash.destroy()
            });
            this.tweens.add({
                targets: ring, radius: Math.max(90, gridWidth * .42), alpha: 0,
                duration: 520, ease: "Cubic.easeOut", onComplete: () => ring.destroy()
            });
            this.burst(x, y, 34);
        }

        addPileStandings(snapshot, state, arenas, items) {
            const roundWins = field(state, "roundWins", {});
            const roundPoints = field(state, "roundPoints", {});
            const performanceViews = field(state, "performanceViews", {});
            const finalViews = field(state, "finalViews", {});
            const resultByPlayer = new Map(field(state, "results", []).map(result =>
                [normalizedId(field(result, "playerId", "")), result]));
            const finalPhase = ["FinalWinner", "WinnerCelebration", "Completed"].includes(snapshot.phase);
            const ranked = arenas.map(arena => {
                const playerId = normalizedId(field(arena, "playerId", ""));
                const result = resultByPlayer.get(playerId);
                return {
                    arena,
                    playerId,
                    name: field(arena, "displayName", "Player"),
                    wins: Number(this.pileValue(roundWins, playerId)),
                    points: Number(this.pileValue(roundPoints, playerId)),
                    views: Number(this.pileValue(performanceViews, playerId)),
                    finalViews: Number(this.pileValue(finalViews, playerId)),
                    roundRank: Number(field(result, "rank", 999)),
                    placementPoints: Number(field(result, "placementPoints", 0))
                };
            }).sort((left, right) => finalPhase
                ? right.wins - left.wins || right.points - left.points || right.views - left.views
                : left.roundRank - right.roundRank);

            const heading = snapshot.phase === "RoundResult" ? "ROUND RESULT"
                : snapshot.phase === "Standings" ? "MATCH STANDINGS"
                    : snapshot.phase === "WinnerCelebration" ? "THE LAST PILE STANDING!"
                        : snapshot.phase === "Completed" ? "FINAL SCRAPYARD RESULTS"
                            : "FINAL SURVIVOR";
            items.push(this.add.text(width / 2, 118, heading, {
                color: "#ffffff", fontFamily: displayFont, fontSize: "48px", fontStyle: "bold",
                stroke: "#ff4fa3", strokeThickness: 9
            }).setOrigin(.5));

            const count = Math.max(1, ranked.length);
            const spacing = Math.min(252, 1040 / count);
            const maximumHeight = 260;
            const minimumHeight = 130;
            const winnerId = normalizedId(field(state, "matchWinnerId", field(field(state, "match", {}), "roundWinnerId", "")));
            const lastRank = ranked.length;
            if (snapshot.phase === "WinnerCelebration") {
                this.addPileWinnerCelebration(snapshot, ranked, winnerId, items);
                return;
            }
            ranked.forEach((entry, index) => {
                const rank = finalPhase ? index + 1 : entry.roundRank;
                const x = width / 2 + (index - (count - 1) / 2) * spacing;
                const podiumHeight = Math.max(minimumHeight, maximumHeight - (rank - 1) * 42);
                const podiumTop = 650 - podiumHeight;
                const colour = rank === 1 ? 0xffc51b : rank === 2 ? 0xbec8da : rank === 3 ? 0xb97745 : 0x36506d;
                const panel = this.add.rectangle(x, podiumTop + podiumHeight / 2, spacing - 18, podiumHeight,
                    colour, .96).setStrokeStyle(5, 0x080914, 1);
                const player = (snapshot.players || []).find(candidate => normalizedId(candidate.playerId) === entry.playerId);
                const avatar = player ? this.avatars.get(player.playerId) : null;
                if (avatar) {
                    avatar.container.setVisible(true).setPosition(x, podiumTop - 1).setScale(.55)
                        .setAlpha(field(entry.arena, "isConnected", true) ? 1 : .42);
                    this.playPileAvatar(avatar,
                        rank === 1 ? "celebrate" : rank === lastRank && lastRank > 1 ? "cry" : "idle");
                }
                items.push(
                    panel,
                    this.add.text(x, podiumTop + 48, entry.name, {
                        color: "#080914", fontFamily: displayFont, fontSize: "20px", fontStyle: "bold",
                        align: "center", wordWrap: { width: spacing - 34 }
                    }).setOrigin(.5),
                    this.add.text(x, podiumTop + 79,
                        finalPhase
                            ? `${entry.wins} ${entry.wins === 1 ? "WIN" : "WINS"} · ${entry.points} RP`
                            : `+${entry.placementPoints} ROUND POINTS`, {
                        color: "#251139", fontFamily: displayFont, fontSize: "14px", fontStyle: "bold",
                        align: "center", wordWrap: { width: spacing - 32 }
                    }).setOrigin(.5),
                    this.add.text(x, 628, `#${rank}`, {
                        color: "#251139", fontFamily: displayFont, fontSize: "27px", fontStyle: "bold"
                    }).setOrigin(.5, 1)
                );
                if (finalPhase) {
                    items.push(this.add.text(x, podiumTop + 106,
                        `${entry.finalViews.toLocaleString()} total views`, {
                        color: "#251139", fontFamily: bodyFont, fontSize: "14px", fontStyle: "bold"
                    }).setOrigin(.5));
                }
            });

            const effectSignature = `${snapshot.gameInstanceId}:${snapshot.phase}:${winnerId}`;
            if (snapshot.phase === "WinnerCelebration" && winnerId &&
                this.pileEffectSignature !== effectSignature) {
                this.pileEffectSignature = effectSignature;
                const winnerIndex = ranked.findIndex(entry => entry.playerId === winnerId);
                if (winnerIndex >= 0) {
                    const x = width / 2 + (winnerIndex - (count - 1) / 2) * spacing;
                    this.burst(x, 270, 55);
                    if (!this.controller.reducedMotion) this.cameras.main.shake(180, .005);
                }
            }
        }

        addPileWinnerCelebration(snapshot, ranked, winnerId, items) {
            const winner = ranked.find(entry => entry.playerId === winnerId) || ranked[0];
            if (!winner) return;
            const winnerPlayer = (snapshot.players || []).find(player =>
                normalizedId(player.playerId) === winner.playerId);
            const winnerAvatar = winnerPlayer ? this.avatars.get(winnerPlayer.playerId) : null;
            const losers = ranked.filter(entry => entry.playerId !== winner.playerId);
            const loserSpacing = Math.min(220, 900 / Math.max(1, losers.length));
            const banner = this.add.container(width + 760, 150).setDepth(110).setAngle(-3);
            const bannerPanel = this.add.rectangle(0, 0, 1040, 150, 0xffc51b, 1)
                .setStrokeStyle(8, 0x080914, 1);
            const bannerText = this.add.text(0, 0, `${winner.name.toUpperCase()} WINS`, {
                color: "#080914", fontFamily: displayFont, fontSize: "64px", fontStyle: "bold",
                stroke: "#ffffff", strokeThickness: 4, align: "center", wordWrap: { width: 960 }
            }).setOrigin(.5);
            banner.add([bannerPanel, bannerText]);
            items.push(banner);
            if (!this.controller.reducedMotion) {
                this.tweens.add({
                    targets: banner,
                    x: width / 2,
                    duration: 550,
                    ease: "Cubic.easeOut",
                    hold: 1300,
                    yoyo: true,
                    onComplete: () => banner.destroy(true)
                });
            } else {
                banner.setPosition(width / 2, 150);
            }

            if (winnerAvatar) {
                winnerAvatar.container.setVisible(true).setDepth(105).setPosition(width / 2, 560).setScale(1.15);
                winnerAvatar.card.setVisible(false);
                winnerAvatar.cardShadow.setVisible(false);
                winnerAvatar.shadow.setVisible(true).setScale(1.6, .8);
                winnerAvatar.presence.setVisible(false);
                winnerAvatar.name.setVisible(true).setY(112).setFontSize(28);
                winnerAvatar.score.setVisible(true).setY(148).setFontSize(23);
                winnerAvatar.wins.setVisible(false);
                winnerAvatar.activity.setVisible(false);
                this.playPileAvatar(winnerAvatar, "celebrate");
            }
            losers.forEach((entry, index) => {
                const player = (snapshot.players || []).find(candidate =>
                    normalizedId(candidate.playerId) === entry.playerId);
                const avatar = player ? this.avatars.get(player.playerId) : null;
                if (!avatar) return;
                const x = width / 2 + (index - (losers.length - 1) / 2) * loserSpacing;
                avatar.container.setVisible(true).setDepth(102).setPosition(x, 600).setScale(.62);
                avatar.card.setVisible(false);
                avatar.cardShadow.setVisible(false);
                avatar.shadow.setVisible(true);
                avatar.presence.setVisible(false);
                avatar.name.setVisible(true).setY(58).setFontSize(20);
                avatar.score.setVisible(false);
                avatar.wins.setVisible(false);
                avatar.activity.setVisible(false);
                this.playPileAvatar(avatar, "cry");
            });
            if (this.pileEffectSignature !== `${snapshot.gameInstanceId}:${snapshot.phase}:${winnerId}`) {
                this.pileEffectSignature = `${snapshot.gameInstanceId}:${snapshot.phase}:${winnerId}`;
                this.burst(width / 2, 360, 80);
                if (!this.controller.reducedMotion) this.cameras.main.shake(220, .006);
            }
        }

        animatePhaseTransition(phase) {
            if (this.controller.reducedMotion) return;
            this.clearPhaseChrome();
            const labels = {
                Drawing: "DRAW!", Guessing: "WHAT IS IT?", Choosing: "PICK AN ANSWER",
                Results: "REVEAL!", ShowdownPlayback: "SHOWDOWN", ShowdownVoting: "VOTE NOW",
                ShowdownResults: "THE WINNER", FinalCelebration: "FINAL RESULTS",
                Introduction: "PILE-UP PANIC", ControllerReady: "READY UP",
                ArenaReveal: "SCRAPYARDS ONLINE", Countdown: "GET READY",
                RoundResult: "ROUND OVER", Standings: "MATCH STANDINGS",
                FinalWinner: "FINAL SURVIVOR", WinnerCelebration: "CHAMPION!"
            };
            const label = labels[phase];
            if (!label) return;
            const band = this.add.rectangle(0, 0, width + 180, 125, 0x090516, .92)
                .setStrokeStyle(5, 0xfde68a, 1);
            const text = this.add.text(0, 0, label, {
                color: "#ffffff", fontFamily: displayFont, fontSize: "58px", fontStyle: "bold",
                stroke: "#db2777", strokeThickness: 8
            }).setOrigin(.5);
            this.phaseChrome = this.add.container(width + 760, height / 2, [band, text]).setDepth(100).setAngle(-3);
            this.tweens.add({
                targets: this.phaseChrome, x: width / 2, duration: 360, ease: "Back.easeOut",
                hold: 520, yoyo: true, onComplete: () => {
                    this.phaseChrome?.destroy(true);
                    this.phaseChrome = null;
                }
            });
            this.cameras.main.shake(120, .004);
        }

        clearPhaseChrome() {
            this.phaseChrome?.destroy(true);
            this.phaseChrome = null;
        }

        applyScreenChrome(snapshot) {
            const signature = JSON.stringify({
                mode: snapshot.mode,
                roomCode: snapshot.roomCode,
                joinUrl: snapshot.joinUrl,
                joinQrDataUri: snapshot.joinQrDataUri,
                gameKey: snapshot.gameKey,
                phase: snapshot.phase,
                phaseEndsAtUtc: snapshot.phaseEndsAtUtc,
                revision: snapshot.revision,
                showRoundRanking: snapshot.showRoundRanking,
                title: snapshot.title,
                prompt: snapshot.prompt,
                phaseMessage: snapshot.phaseMessage,
                entries: snapshot.entries,
                presenterMessage: snapshot.presenterMessage,
                hasDrawing: Boolean(snapshot.drawing?.animations?.length),
                media: snapshot.media
            });
            if (signature === this.screenChromeSignature) return;
            this.screenChromeSignature = signature;
            this.deadlineTimer?.remove(false);
            this.deadlineTimer = null;
            this.screenChromeContainer?.destroy(true);
            this.screenChromeContainer = null;

            if (["pile-up-panic", "voicechoon"].includes(snapshot.gameKey)) return;
            if (snapshot.showRoundRanking || snapshot.presenterMessage) return;
            const items = [];
            if (snapshot.mode === "Pairing") {
                items.push(
                    this.add.text(width / 2, 125, "QUIZIZZO", {
                        color: "#ffffff", fontFamily: displayFont, fontSize: "84px", fontStyle: "bold",
                        stroke: "#130828", strokeThickness: 10
                    }).setOrigin(.5),
                    this.add.text(width / 2, 305, "START YOUR PARTY", {
                        color: "#fde68a", fontFamily: displayFont, fontSize: "48px", fontStyle: "bold"
                    }).setOrigin(.5),
                    this.add.text(width / 2, 375,
                        "Sign in at quizizzo.com and open Host display", {
                        color: "#ffffff", fontFamily: bodyFont, fontSize: "27px", fontStyle: "bold"
                    }).setOrigin(.5)
                );
                this.screenChromeContainer = this.add.container(0, 0, items).setDepth(55);
                return;
            }

            if (snapshot.mode === "Lobby") {
                items.push(
                    this.add.text(width / 2, 42, "GRAB A PHONE · MAKE A PLAYER", {
                        color: "#ffffff", backgroundColor: "#db2777", padding: { x: 18, y: 8 },
                        fontFamily: displayFont, fontSize: "18px", fontStyle: "bold", letterSpacing: 3
                    }).setOrigin(.5),
                    this.add.text(width / 2, 93, "JOIN THE PARTY", {
                        color: "#ffffff", fontFamily: displayFont, fontSize: "52px", fontStyle: "bold",
                        stroke: "#130828", strokeThickness: 8
                    }).setOrigin(.5),
                    this.add.text(width / 2, 160, snapshot.roomCode || "", {
                        color: "#ffffff", fontFamily: displayFont, fontSize: "72px", fontStyle: "bold",
                        letterSpacing: 10, stroke: "#130828", strokeThickness: 9
                    }).setOrigin(.5)
                );
                const qrPanel = this.add.rectangle(width / 2, 335, 224, 224, 0xffffff, 1)
                    .setStrokeStyle(9, 0xf8f7ff, 1);
                items.push(qrPanel);
                const qrKey = `join-qr-${snapshot.roomCode || "none"}`;
                if (snapshot.joinQrDataUri && this.textures.exists(qrKey)) {
                    items.push(this.add.image(width / 2, 335, qrKey).setDisplaySize(198, 198));
                } else if (snapshot.joinQrDataUri) {
                    items.push(this.add.text(width / 2, 335, "Loading…", {
                        color: "#24123f", fontFamily: bodyFont, fontSize: "20px", fontStyle: "bold"
                    }).setOrigin(.5));
                    this.loadQrTexture(qrKey, snapshot.joinQrDataUri, signature);
                }
                const joinLink = this.add.text(width / 2, 475,
                    snapshot.joinUrl ? `${snapshot.joinUrl} ↗` : "", {
                    color: "#bae6fd", fontFamily: bodyFont, fontSize: "18px",
                    backgroundColor: "#17123d", padding: { x: 14, y: 7 }
                }).setOrigin(.5);
                if (snapshot.joinUrl) {
                    joinLink.setInteractive({ useHandCursor: true });
                    joinLink.on("pointerover", () => joinLink.setColor("#ffffff"));
                    joinLink.on("pointerdown", () => joinLink.setScale(.97));
                    joinLink.on("pointerout", () => joinLink.setColor("#bae6fd").setScale(1));
                    joinLink.on("pointerup", () => {
                        joinLink.setScale(1);
                        window.open(snapshot.joinUrl, "_blank", "noopener,noreferrer");
                    });
                }
                items.push(joinLink);
                this.screenChromeContainer = this.add.container(0, 0, items).setDepth(55);
                return;
            }

            const briefing = ["Briefing", "ShowdownBriefing"].includes(snapshot.phase);
            if (!briefing) {
                const compactShowdownHeader = ["ShowdownPlayback", "ShowdownVoting"]
                    .includes(snapshot.phase);
                if (compactShowdownHeader) {
                    const headerPanel = this.add.graphics();
                    headerPanel.fillStyle(0x09051f, .7);
                    headerPanel.fillRoundedRect(165, 8, 950, 112, 22);
                    headerPanel.lineStyle(2, 0xffffff, .14);
                    headerPanel.strokeRoundedRect(165, 8, 950, 112, 22);
                    items.push(headerPanel);
                }
                if (snapshot.title) {
                    items.push(this.add.text(width / 2, compactShowdownHeader ? 20 : 22,
                        snapshot.title.toUpperCase(), {
                        color: "#fde68a", fontFamily: displayFont,
                        fontSize: compactShowdownHeader ? "16px" : "18px",
                        fontStyle: "bold", letterSpacing: 3
                    }).setOrigin(.5));
                }
                if (snapshot.prompt) {
                    items.push(this.add.text(width / 2, compactShowdownHeader ? 55 : 53,
                        snapshot.prompt, {
                        color: "#ffffff", fontFamily: displayFont,
                        fontSize: compactShowdownHeader ? "22px" : "31px", fontStyle: "bold",
                        align: "center", wordWrap: { width: compactShowdownHeader ? 900 : 870 },
                        stroke: "#24123f", strokeThickness: compactShowdownHeader ? 4 : 6
                    }).setOrigin(.5));
                }
                if (snapshot.phaseMessage) {
                    items.push(this.add.text(width / 2, compactShowdownHeader ? 94 : 87,
                        snapshot.phaseMessage, {
                        color: compactShowdownHeader ? "#dff9ff" : "#ffffff",
                        backgroundColor: compactShowdownHeader ? "#312e81" : undefined,
                        padding: compactShowdownHeader ? { x: 11, y: 4 } : undefined,
                        fontFamily: bodyFont,
                        fontSize: compactShowdownHeader ? "14px" : "17px", fontStyle: "bold",
                        align: "center", wordWrap: { width: 800 }
                    }).setOrigin(.5));
                }
                this.addDeadline(snapshot.phaseEndsAtUtc, items);
                this.addGameMedia(snapshot, items, signature);
                this.addEntryCards(snapshot, items);
            }
            if (briefing) {
                this.addDeadline(snapshot.phaseEndsAtUtc, items);
            }
            this.screenChromeContainer = this.add.container(0, 0, items).setDepth(55);
        }

        loadQrTexture(key, dataUri, expectedSignature) {
            if (this.qrLoadPending === key) return;
            this.qrLoadPending = key;
            const image = new Image();
            image.onload = () => {
                this.qrLoadPending = null;
                if (!this.textures.exists(key)) this.textures.addImage(key, image);
                if (this.screenChromeSignature === expectedSignature && this.scene?.isActive()) {
                    this.screenChromeSignature = null;
                    this.applyScreenChrome(this.controller.snapshot);
                }
            };
            image.onerror = () => { this.qrLoadPending = null; };
            image.src = dataUri;
        }

        addDeadline(endsAtUtc, items) {
            if (!endsAtUtc) return;
            const deadline = this.add.text(1205, 48, "", {
                color: "#fff4a8", backgroundColor: "#17123d", padding: { x: 14, y: 9 },
                fontFamily: displayFont, fontSize: "25px", fontStyle: "bold"
            }).setOrigin(1, .5).setStroke("#ffffff", 1);
            const update = () => {
                const remaining = Math.max(0, Math.ceil((Date.parse(endsAtUtc) - Date.now()) / 1000));
                deadline.setText(`${remaining}s`);
            };
            update();
            this.deadlineTimer = this.time.addEvent({ delay: 250, loop: true, callback: update });
            items.push(deadline);
        }

        addGameMedia(snapshot, items, expectedSignature) {
            const mediaItems = snapshot.media?.items || [];
            if (!mediaItems.length) return;
            if (snapshot.media.mode === "comment-feed") {
                this.addSlopCommentFeed(snapshot, mediaItems.slice(0, 4), items, expectedSignature);
                return;
            }
            const gallery = snapshot.media.mode === "gallery";
            const shown = mediaItems.slice(0, gallery ? 6 : 1);
            const columns = gallery ? Math.min(3, shown.length) : 1;
            const rows = Math.ceil(shown.length / columns);
            const heroHasEntries = !gallery && (snapshot.entries?.length || 0) > 0;
            const cardWidth = gallery ? 330 : heroHasEntries ? 520 : 620;
            const cardHeight = gallery ? (rows > 1 ? 210 : 285) : heroHasEntries ? 410 : 460;
            const imageHeight = gallery ? (rows > 1 ? 145 : 210) : heroHasEntries ? 325 : 390;
            const centreY = gallery ? (rows > 1 ? 335 : 350) : 350;
            const gap = gallery ? 18 : 0;
            shown.forEach((media, index) => {
                const row = Math.floor(index / columns);
                const column = index % columns;
                const inRow = Math.min(columns, shown.length - row * columns);
                const mediaCentreX = heroHasEntries ? 330 : width / 2;
                const x = mediaCentreX + (column - (inRow - 1) / 2) * (cardWidth + gap);
                const y = centreY + (row - (rows - 1) / 2) * (cardHeight + gap);
                const shadow = this.add.rectangle(x + 8, y + 10, cardWidth, cardHeight, 0x090516, .38);
                const panel = this.add.rectangle(x, y, cardWidth, cardHeight, 0xfffbeb, 1)
                    .setStrokeStyle(6, snapshot.gameKey === "slop-machine" ? 0xffd400 : 0x24123f, 1);
                items.push(shadow, panel);
                if (snapshot.gameKey === "slop-machine" && snapshot.phase.endsWith("Results") &&
                    !this.controller.reducedMotion) {
                    panel.setScale(.92);
                    this.tweens.add({
                        targets: panel, scaleX: 1, scaleY: 1,
                        duration: 420, delay: index * 90, ease: "Back.easeOut"
                    });
                }
                if (media.imageUrl) {
                    const key = `game-media-${media.id}`;
                    if (this.textures.exists(key)) {
                        const imageY = y - cardHeight / 2 + imageHeight / 2 + 12;
                        const image = this.add.image(x, imageY, key);
                        this.fitImageWithin(image, cardWidth - 24, imageHeight);
                        items.push(image);
                    } else {
                        items.push(this.add.text(x, y - 20, "PROCESSING THUMBNAIL…", {
                            color: "#24123f", fontFamily: displayFont, fontSize: "18px", fontStyle: "bold"
                        }).setOrigin(.5));
                        this.loadMediaTexture(key, media.imageUrl, expectedSignature);
                    }
                }
                if (media.heading) {
                    items.push(this.add.text(x, y + cardHeight / 2 - 40, media.heading, {
                        color: "#24123f", fontFamily: displayFont,
                        fontSize: gallery ? "16px" : "22px", fontStyle: "bold",
                        align: "center", wordWrap: { width: cardWidth - 28 }
                    }).setOrigin(.5));
                }
                if (media.body) {
                    items.push(this.add.text(x, y + cardHeight / 2 - 15, media.body, {
                        color: "#5b2449", fontFamily: bodyFont, fontSize: "13px",
                        align: "center", wordWrap: { width: cardWidth - 28 }
                    }).setOrigin(.5));
                }
                if (media.badge) {
                    items.push(this.add.text(x + cardWidth / 2 - 14, y - cardHeight / 2 + 14,
                        media.badge, {
                        color: "#ffffff", backgroundColor: "#ef2b6e", padding: { x: 8, y: 4 },
                        fontFamily: displayFont, fontSize: "12px", fontStyle: "bold"
                    }).setOrigin(1, 0));
                }
            });
        }

        addSlopCommentFeed(snapshot, mediaItems, items, expectedSignature) {
            const columns = mediaItems.length === 1 ? 1 : 2;
            const rows = Math.ceil(mediaItems.length / columns);
            const cardWidth = columns === 1 ? 700 : 500;
            const cardHeight = rows === 1 ? 390 : 226;
            const imageHeight = rows === 1 ? 210 : 112;
            const startY = rows === 1 ? 345 : 245;
            mediaItems.forEach((media, index) => {
                const row = Math.floor(index / columns);
                const column = index % columns;
                const inRow = Math.min(columns, mediaItems.length - row * columns);
                const x = width / 2 + (column - (inRow - 1) / 2) * (cardWidth + 18);
                const y = startY + row * (cardHeight + 16);
                const shadow = this.add.rectangle(x + 7, y + 9, cardWidth, cardHeight, 0x05020f, .42);
                const panel = this.add.rectangle(x, y, cardWidth, cardHeight, 0x18122f, .98)
                    .setStrokeStyle(media.badge === "PINNED COMMENT" ? 5 : 3,
                        media.badge === "PINNED COMMENT" ? 0xffd400 : 0x7c3aed, 1);
                items.push(shadow, panel);
                const key = `game-media-${media.id}`;
                if (media.imageUrl && this.textures.exists(key)) {
                    const image = this.add.image(x, y - cardHeight / 2 + imageHeight / 2 + 10, key);
                    this.fitImageWithin(image, cardWidth - 20, imageHeight);
                    items.push(image);
                } else if (media.imageUrl) {
                    this.loadMediaTexture(key, media.imageUrl, expectedSignature);
                }
                const titleY = y - cardHeight / 2 + imageHeight + 24;
                items.push(this.add.text(x - cardWidth / 2 + 18, titleY, media.heading || "", {
                    color: "#ffffff", fontFamily: displayFont, fontSize: rows === 1 ? "19px" : "15px",
                    fontStyle: "bold", wordWrap: { width: cardWidth - 36 }
                }).setOrigin(0, 0));
                const bubbleY = y + cardHeight / 2 - (rows === 1 ? 64 : 48);
                const bubble = this.add.rectangle(x, bubbleY, cardWidth - 32,
                    rows === 1 ? 82 : 64, 0xffffff, .1).setStrokeStyle(2, 0xffffff, .16);
                const comment = this.add.text(x - cardWidth / 2 + 30, bubbleY,
                    media.body || "", {
                    color: "#f8fafc", fontFamily: bodyFont,
                    fontSize: rows === 1 ? "18px" : "14px", fontStyle: "bold",
                    wordWrap: { width: cardWidth - 60 }
                }).setOrigin(0, .5);
                items.push(bubble, comment);
                if (media.badge) {
                    items.push(this.add.text(x + cardWidth / 2 - 18, y - cardHeight / 2 + 14,
                        media.badge, {
                        color: media.badge === "PINNED COMMENT" ? "#24123f" : "#ffffff",
                        backgroundColor: media.badge === "PINNED COMMENT" ? "#ffd400" : "#ef2b6e",
                        padding: { x: 8, y: 4 }, fontFamily: displayFont,
                        fontSize: "11px", fontStyle: "bold"
                    }).setOrigin(1, 0));
                }
            });
        }

        fitImageWithin(image, maximumWidth, maximumHeight) {
            const sourceWidth = Math.max(1, image.width);
            const sourceHeight = Math.max(1, image.height);
            image.setScale(Math.min(maximumWidth / sourceWidth, maximumHeight / sourceHeight));
            return image;
        }

        loadMediaTexture(key, imageUrl, expectedSignature) {
            if (this.textures.exists(key)) return;
            const image = new Image();
            image.onload = () => {
                if (!this.textures.exists(key)) this.textures.addImage(key, image);
                if (this.screenChromeSignature === expectedSignature && this.scene?.isActive()) {
                    this.screenChromeSignature = null;
                    this.applyScreenChrome(this.controller.snapshot);
                }
            };
            image.src = imageUrl;
        }

        addEntryCards(snapshot, items) {
            const phasesWithEntries = [
                "Choosing", "Results", "Voting", "ShowdownVoting", "ShowdownResults",
                "FreshSlopVoting", "FreshSlopResults",
                "FinalVoting", "FinalMachineGuess", "FinalResults"
            ];
            const entries = snapshot.entries || [];
            if (!phasesWithEntries.includes(snapshot.phase) || entries.length === 0) return;
            if (snapshot.gameKey === "slop-machine" && snapshot.media?.mode === "hero") {
                this.addSlopSideEntries(snapshot, entries, items);
                return;
            }
            if (snapshot.phase === "ShowdownResults" && snapshot.drawing?.animations?.length) {
                // Creator, votes, points, and rank are integrated into each reveal card.
                // A second results layer would cover the animation itself.
                return;
            }
            const besideDrawing = Boolean(snapshot.drawing?.animations?.length)
                && ["Choosing", "Results"].includes(snapshot.phase);
            if (besideDrawing) {
                this.addAniMatesSideEntries(snapshot, entries.slice(0, 6), items);
                return;
            }
            const columns = Math.min(3, entries.length);
            const cardWidth = Math.min(340, 1040 / columns - 18);
            const cardHeight = entries.length > columns ? 112 : 140;
            const centreX = width / 2;
            const startY = 185;
            entries.slice(0, 6).forEach((entry, index) => {
                const row = Math.floor(index / columns);
                const itemsInRow = Math.min(columns, entries.length - row * columns);
                const column = index % columns;
                const x = centreX + (column - (itemsInRow - 1) / 2) * (cardWidth + 15);
                const y = startY + row * (cardHeight + 15);
                const panel = this.add.rectangle(x, y, cardWidth, cardHeight, 0xfffbeb, .98)
                    .setStrokeStyle(4, 0x24123f, 1);
                const label = this.add.text(x, y - cardHeight / 2 + 14, entry.label || "", {
                    color: "#24123f", fontFamily: displayFont, fontSize: "22px", fontStyle: "bold",
                    align: "center", wordWrap: { width: cardWidth - 22 }
                }).setOrigin(.5, 0);
                const value = this.add.text(x, y + 10, entry.value || "", {
                    color: "#17131f", fontFamily: bodyFont, fontSize: "16px", fontStyle: "bold",
                    align: "center", wordWrap: { width: cardWidth - 22 }
                }).setOrigin(.5);
                items.push(panel, label, value);
                if (entry.rank != null) {
                    items.push(this.add.text(x, y + cardHeight / 2 - 13,
                        `#${entry.rank} · +${this.scoreLabel(entry.pointsAwarded, snapshot)}`, {
                        color: "#7c2d92", fontFamily: displayFont, fontSize: "15px", fontStyle: "bold"
                    }).setOrigin(.5, 1));
                }
            });
        }

        addAniMatesSideEntries(snapshot, entries, items) {
            const centreX = 900;
            const centreY = 322;
            const boardWidth = 630;
            const boardHeight = 350;
            const boardShadow = this.add.graphics();
            boardShadow.fillStyle(0x090516, .34);
            boardShadow.fillRoundedRect(
                centreX - boardWidth / 2 + 10, centreY - boardHeight / 2 + 12,
                boardWidth, boardHeight, 24);
            const board = this.add.graphics();
            board.fillStyle(0x09051f, .7);
            board.fillRoundedRect(
                centreX - boardWidth / 2, centreY - boardHeight / 2,
                boardWidth, boardHeight, 24);
            board.lineStyle(3, 0x67e8f9, .65);
            board.strokeRoundedRect(
                centreX - boardWidth / 2, centreY - boardHeight / 2,
                boardWidth, boardHeight, 24);
            const kicker = this.add.text(centreX - boardWidth / 2 + 24,
                centreY - boardHeight / 2 + 18,
                snapshot.phase === "Results" ? "THE ANSWERS" : "PICK YOUR ANSWER", {
                color: "#fff4a8", fontFamily: displayFont, fontSize: "18px",
                fontStyle: "bold", letterSpacing: 2
            }).setOrigin(0, 0);
            const columns = entries.length <= 3 ? 1 : 2;
            const rows = Math.ceil(entries.length / columns);
            const gap = 12;
            const cardWidth = columns === 1 ? 574 : 281;
            const availableHeight = boardHeight - 78;
            const cardHeight = Math.min(112,
                Math.floor((availableHeight - gap * (rows - 1)) / rows));
            const startY = centreY - boardHeight / 2 + 65 + cardHeight / 2;
            items.push(boardShadow, board, kicker);

            entries.forEach((entry, index) => {
                const row = Math.floor(index / columns);
                const column = index % columns;
                const itemsInRow = Math.min(columns, entries.length - row * columns);
                const x = centreX + (column - (itemsInRow - 1) / 2) * (cardWidth + gap);
                const y = startY + row * (cardHeight + gap);
                const shadow = this.add.graphics();
                shadow.fillStyle(0x090516, .32);
                shadow.fillRoundedRect(
                    x - cardWidth / 2 + 6, y - cardHeight / 2 + 7,
                    cardWidth, cardHeight, 14);
                const panel = this.add.graphics();
                panel.fillStyle(0xfffbeb, 1);
                panel.fillRoundedRect(x - cardWidth / 2, y - cardHeight / 2,
                    cardWidth, cardHeight, 14);
                panel.lineStyle(3, 0x24123f, 1);
                panel.strokeRoundedRect(x - cardWidth / 2, y - cardHeight / 2,
                    cardWidth, cardHeight, 14);
                const badgeX = x - cardWidth / 2 + 38;
                const badge = this.add.circle(badgeX, y, 23,
                    index % 2 === 0 ? 0xdb2777 : 0x7c3aed, 1)
                    .setStrokeStyle(3, 0xffffff, .7);
                const label = this.add.text(badgeX, y, entry.label || "", {
                    color: "#ffffff", fontFamily: displayFont,
                    fontSize: "21px", fontStyle: "bold"
                }).setOrigin(.5);
                const value = this.add.text(x - cardWidth / 2 + 74, y,
                    entry.value || "", {
                    color: "#17131f", fontFamily: displayFont,
                    fontSize: columns === 1 ? "21px" : "17px", fontStyle: "bold",
                    wordWrap: { width: cardWidth - 96 }, align: "left"
                }).setOrigin(0, .5);
                items.push(shadow, panel, badge, label, value);
                if (entry.rank != null) {
                    items.push(this.add.text(x + cardWidth / 2 - 14,
                        y + cardHeight / 2 - 9,
                        `#${entry.rank} · +${this.scoreLabel(entry.pointsAwarded, snapshot)}`, {
                        color: "#7c2d92", fontFamily: displayFont,
                        fontSize: "13px", fontStyle: "bold"
                    }).setOrigin(1, 1));
                }
            });
        }

        addSlopSideEntries(snapshot, entries, items) {
            const centreX = 935;
            const boardWidth = 570;
            const boardHeight = 455;
            const top = 126;
            const columns = entries.length > 6 ? 2 : 1;
            const rows = Math.ceil(entries.length / columns);
            const gap = 8;
            const cardWidth = columns === 1 ? 526 : 255;
            const cardHeight = Math.min(64, (boardHeight - 66 - gap * (rows - 1)) / rows);
            const board = this.add.graphics();
            board.fillStyle(0x071519, .9);
            board.fillRoundedRect(centreX - boardWidth / 2, top, boardWidth, boardHeight, 20);
            board.lineStyle(4, 0x00e7d7, .8);
            board.strokeRoundedRect(centreX - boardWidth / 2, top, boardWidth, boardHeight, 20);
            const heading = this.add.text(centreX, top + 20,
                snapshot.phase.endsWith("Results") ? "CREATORS REVEALED" : "THE CONTENT FEED", {
                color: "#ffd400", fontFamily: displayFont, fontSize: "19px",
                fontStyle: "bold", letterSpacing: 2
            }).setOrigin(.5, 0);
            items.push(board, heading);
            entries.forEach((entry, index) => {
                const row = Math.floor(index / columns);
                const column = index % columns;
                const inRow = Math.min(columns, entries.length - row * columns);
                const x = centreX + (column - (inRow - 1) / 2) * (cardWidth + gap);
                const y = top + 58 + row * (cardHeight + gap) + cardHeight / 2;
                const panel = this.add.rectangle(x, y, cardWidth, cardHeight, 0xfffbeb, 1)
                    .setStrokeStyle(2, index % 2 ? 0xef2b6e : 0xffd400, 1);
                const winner = snapshot.phase.endsWith("Results") && entry.rank === 1;
                if (winner) {
                    panel.setStrokeStyle(4, 0xffd400, 1);
                    if (!this.controller.reducedMotion) {
                        panel.setScale(.94);
                        this.tweens.add({
                            targets: panel, scaleX: 1.025, scaleY: 1.025,
                            duration: 520, ease: "Back.easeOut"
                        });
                    }
                }
                const label = this.add.text(x - cardWidth / 2 + 12, y, entry.label || "", {
                    color: "#ef2b6e", fontFamily: displayFont,
                    fontSize: columns === 1 ? "17px" : "14px", fontStyle: "bold"
                }).setOrigin(0, .5);
                const value = this.add.text(x - cardWidth / 2 + 48, y, entry.value || "", {
                    color: "#24123f", fontFamily: bodyFont,
                    fontSize: columns === 1 ? "15px" : "12px", fontStyle: "bold",
                    wordWrap: { width: cardWidth - 62 }
                }).setOrigin(0, .5);
                items.push(panel, label, value);
                if (snapshot.phase.endsWith("Results")) {
                    const views = this.add.text(x + cardWidth / 2 - 10,
                        y + cardHeight / 2 - 7, `+${this.scoreLabel(0, snapshot)}`, {
                        color: winner ? "#9a3412" : "#7c2d92",
                        fontFamily: displayFont, fontSize: columns === 1 ? "13px" : "11px",
                        fontStyle: "bold"
                    }).setOrigin(1, 1);
                    items.push(views);
                    if (this.controller.reducedMotion) {
                        views.setText(`+${this.scoreLabel(entry.pointsAwarded || 0, snapshot)}`);
                    } else {
                        this.tweens.addCounter({
                            from: 0, to: entry.pointsAwarded || 0, duration: 700,
                            delay: 180 + index * 90, ease: "Cubic.easeOut",
                            onUpdate: tween => views.setText(
                                `+${this.scoreLabel(Math.round(tween.getValue()), snapshot)}`)
                        });
                    }
                }
            });
        }

        startShowdownReveal(drawing) {
            const animations = drawing.animations || [];
            if (animations.length === 0) return;
            const grid = this.showdownGrid(animations);
            const frameSize = Math.max(72, Math.min(grid.cardWidth - 24, grid.cardHeight - 82));
            const items = [];
            const cards = [];
            animations.forEach((animation, index) => {
                const row = Math.floor(index / grid.columns);
                const column = index % grid.columns;
                const x = (column - (Math.min(grid.columns, animations.length - row * grid.columns) - 1) / 2)
                    * (grid.cardWidth + grid.gapX);
                const y = row * (grid.cardHeight + grid.gapY);
                const shadow = this.add.rectangle(7, 9, grid.cardWidth, grid.cardHeight, 0x090516, .38);
                const panel = this.add.rectangle(0, 0, grid.cardWidth, grid.cardHeight, 0xfffbeb, .99)
                    .setStrokeStyle(animation.rank === 1 ? 9 : 5,
                        animation.rank === 1 ? 0xfacc15 : 0x24123f, 1);
                const frameUrl = animation.frameUrls[0];
                const frame = this.add.image(0, grid.rows > 1 ? -18 : -25,
                    `drawing-${frameUrl.split("/").pop()}`)
                    .setDisplaySize(frameSize, frameSize);
                const caption = this.add.text(0, grid.cardHeight / 2 - (grid.rows > 1 ? 36 : 48),
                    `${animation.prompt} — ${animation.creatorName || "?"}`, {
                    color: "#24123f", fontFamily: displayFont,
                    fontSize: grid.rows > 1 ? "13px" : "18px",
                    fontStyle: "bold", align: "center", wordWrap: { width: grid.cardWidth - 20 }
                }).setOrigin(.5);
                const result = this.add.text(0, grid.cardHeight / 2 - (grid.rows > 1 ? 15 : 20),
                    `${animation.votes} vote(s) · +${Number(animation.pointsAwarded || 0).toLocaleString()} pts`, {
                    color: animation.rank === 1 ? "#9a3412" : "#6b21a8",
                    fontFamily: displayFont, fontSize: grid.rows > 1 ? "11px" : "14px",
                    fontStyle: "bold"
                }).setOrigin(.5);
                const badge = this.add.text(-grid.cardWidth / 2 + 10, -grid.cardHeight / 2 + 10,
                    animation.prompt, {
                    color: "#ffffff", backgroundColor: animation.rank === 1 ? "#db2777" : "#312e81",
                    padding: { x: 9, y: 5 }, fontFamily: displayFont,
                    fontSize: grid.rows > 1 ? "13px" : "18px", fontStyle: "bold"
                }).setOrigin(0, 0).setAngle(-2);
                const rank = this.add.text(grid.cardWidth / 2 - 12, -grid.cardHeight / 2 + 12,
                    `#${animation.rank || "–"}`, {
                    color: "#24123f", backgroundColor: animation.rank === 1 ? "#fde68a" : "#e9d5ff",
                    padding: { x: 9, y: 5 }, fontFamily: displayFont,
                    fontSize: grid.rows > 1 ? "12px" : "16px", fontStyle: "bold"
                }).setOrigin(1, 0).setAngle(2);
                const card = this.add.container(x, y,
                    [shadow, panel, frame, caption, result, badge, rank]);
                items.push(card);
                cards.push({ animation, frame, frameIndex: 0, card });
                if (!this.controller.reducedMotion) {
                    card.setScale(.8).setAlpha(0);
                    this.tweens.add({
                        targets: card,
                        scale: 1,
                        alpha: 1,
                        delay: index * 110,
                        duration: 460,
                        ease: "Cubic.easeOut"
                    });
                }
            });
            this.drawingContainer = this.add.container(width / 2, 138 + grid.cardHeight / 2, items).setDepth(12);
            const show = () => cards.forEach(card => {
                const url = card.animation.frameUrls[card.frameIndex];
                card.frame.setTexture(`drawing-${url.split("/").pop()}`);
                card.frameIndex = (card.frameIndex + 1) % card.animation.frameUrls.length;
            });
            show();
            if (!this.controller.reducedMotion) {
                this.drawingTimer = this.time.addEvent({
                    delay: Math.max(100, drawing.frameDurationMilliseconds || 300),
                    loop: true,
                    callback: show
                });
            }
        }

        startRoundRanking(snapshot, initial) {
            const players = playerMap(snapshot);
            const signature = JSON.stringify({
                phase: snapshot.phase,
                revision: snapshot.revision,
                results: snapshot.results,
                scores: snapshot.results.map(result => players.get(result.playerId)?.score || 0),
                statistics: snapshot.statistics
            });
            if (signature === this.roundRankingSignature) return;

            this.stopRoundRanking();
            this.clearPhaseChrome();
            this.roundRankingSignature = signature;
            this.roundRankingStartScores = new Map(snapshot.players.map(player => [
                player.playerId,
                Math.max(0, player.score -
                    (snapshot.results.find(result => result.playerId === player.playerId)?.pointsAwarded || 0))
            ]));
            this.renderPodium({ ...snapshot, showRoundRanking: false });
            this.avatars.forEach(avatar => {
                avatar.rig?.stop();
                avatar.container.setVisible(false);
            });
            const reveal = () => {
                if (this.roundRankingSignature !== signature || !this.scene?.isActive()) return;
                this.roundRankingTimer = null;
                this.applyPresenter(null);
                const podiumChanged = this.renderPodium(snapshot);
                this.layoutAvatars(snapshot, initial || this.controller.reducedMotion, podiumChanged);
                if (!initial && !this.controller.reducedMotion) {
                    this.countRoundScores(snapshot, signature);
                }
            };
            if (initial || this.controller.reducedMotion) {
                reveal();
                return;
            }
            const presenterLine = snapshot.gameKey === "animates"
                && snapshot.phase === "FinalCelebration"
                ? "That's AniMates! Let's crown our animation champions!"
                : "That's another round over — let's see how the scores look!";
            this.applyPresenter(presenterLine);
            this.roundRankingTimer = this.time.delayedCall(2800, reveal);
        }

        stopRoundRanking() {
            if (!this.roundRankingSignature && !this.roundRankingTimer) return;
            this.roundRankingTimer?.remove(false);
            this.roundRankingTimer = null;
            this.roundRankingScoreTimers.splice(0).forEach(timer => timer.remove(false));
            this.roundRankingScoreTweens.splice(0).forEach(tween => tween.stop());
            this.roundRankingStartScores.clear();
            this.roundRankingSignature = null;
            this.avatars.forEach(avatar => avatar.rig?.stop());
        }

        countRoundScores(snapshot, signature) {
            const players = playerMap(snapshot);
            const ordered = [...snapshot.results].sort((left, right) => right.rank - left.rank);
            let delay = 1050;
            ordered.forEach(result => {
                const player = players.get(result.playerId);
                const avatar = this.avatars.get(result.playerId);
                if (!player || !avatar) return;
                const start = this.roundRankingStartScores.get(result.playerId) ?? player.score;
                const difference = Math.max(0, player.score - start);
                const duration = difference > 0 ? Math.min(1300, 520 + difference * .55) : 420;
                avatar.score.setText(this.scoreLabel(start, snapshot));
                const timer = this.time.delayedCall(delay, () => {
                    if (this.roundRankingSignature !== signature) return;
                    const counter = { value: start };
                    const tween = this.tweens.add({
                        targets: counter,
                        value: player.score,
                        duration,
                        ease: "Cubic.easeOut",
                        onUpdate: () => avatar.score.setText(
                            this.scoreLabel(Math.round(counter.value), snapshot)),
                        onComplete: () => {
                            avatar.score.setText(this.scoreLabel(player.score, snapshot));
                            this.tweens.add({
                                targets: avatar.score, scale: 1.45,
                                duration: 150, yoyo: true, ease: "Back.easeOut"
                            });
                        }
                    });
                    this.roundRankingScoreTweens.push(tween);
                });
                this.roundRankingScoreTimers.push(timer);
                delay += duration + 240;
            });
        }

        renderPodium(snapshot) {
            const isPodium = snapshot.showRoundRanking && snapshot.results?.length;
            const players = playerMap(snapshot);
            const signature = isPodium ? JSON.stringify({
                phase: snapshot.phase,
                results: snapshot.results,
                scores: snapshot.results.map(result => players.get(result.playerId)?.score || 0)
            }) : null;
            if (signature === this.podiumSignature) {
                return false;
            }
            if (this.podiumContainer) {
                this.tweens.killTweensOf(this.podiumContainer.getAll());
                this.podiumContainer.destroy(true);
                this.podiumContainer = null;
            }
            this.podiumSignature = signature;
            if (!isPodium) {
                return true;
            }
            const ordered = [...snapshot.results].sort((left, right) => left.rank - right.rank);
            const maximum = Math.max(1, ...ordered.map(result => players.get(result.playerId)?.score || 0));
            const spacing = Math.min(180, 1080 / ordered.length);
            const widthPerPodium = Math.max(72, spacing - 12);
            const slopCelebration = snapshot.phase === "WinnerCelebration";
            const aniMatesCelebration = snapshot.gameKey === "animates"
                && snapshot.phase === "FinalCelebration";
            const celebration = slopCelebration || aniMatesCelebration;
            const previousOrder = ordered.map(result => ({
                playerId: result.playerId,
                score: Math.max(0, (players.get(result.playerId)?.score || 0) -
                    (result.pointsAwarded || 0)),
                name: players.get(result.playerId)?.displayName || ""
            })).sort((left, right) => right.score - left.score || left.name.localeCompare(right.name));
            const previousRanks = new Map();
            let previousScore = null;
            let previousRank = 0;
            previousOrder.forEach((player, index) => {
                if (player.score !== previousScore) {
                    previousRank = index + 1;
                    previousScore = player.score;
                }
                previousRanks.set(player.playerId, previousRank);
            });
            const biggestGain = Math.max(0, ...ordered.map(result => result.pointsAwarded || 0));
            const biggestGainers = ordered
                .filter(result => biggestGain > 0 && result.pointsAwarded === biggestGain)
                .map(result => players.get(result.playerId)?.displayName)
                .filter(Boolean);
            const items = [
                this.add.text(width / 2, 56,
                    aniMatesCelebration ? "FINAL RESULTS"
                        : slopCelebration ? "FINAL CHANNEL RANK" : "ROUND COMPLETE", {
                    color: "#fde68a", fontFamily: displayFont, fontSize: "24px", fontStyle: "bold",
                    letterSpacing: 5
                }).setOrigin(.5),
                this.add.text(width / 2, 96,
                    aniMatesCelebration ? "ANIMATES CHAMPIONS"
                        : slopCelebration ? "THE ALGORITHM HAS CHOSEN ITS HUMAN" : "CURRENT STANDINGS", {
                    color: "#ffffff", fontFamily: displayFont, fontSize: "46px", fontStyle: "bold",
                    stroke: "#24123f", strokeThickness: 7
                }).setOrigin(.5)
            ];
            const subheading = aniMatesCelebration
                ? "THE FINAL SCORES ARE IN"
                : slopCelebration
                    ? snapshot.prompt
                    : biggestGainers.length
                        ? `BIGGEST GAINER: ${biggestGainers.join(" & ")} · +${this.scoreLabel(biggestGain, snapshot)}`
                        : "THE FEED REFRESHED WITHOUT MERCY";
            items.push(this.add.text(width / 2, 142, subheading, {
                color: celebration ? "#67e8f9" : "#fff4a8",
                fontFamily: displayFont,
                fontSize: celebration ? "20px" : "17px",
                fontStyle: "bold",
                align: "center",
                wordWrap: { width: 1080 }
            }).setOrigin(.5));
            if (aniMatesCelebration) {
                this.addFinalStatistics(snapshot.statistics || [], items);
            }
            ordered.forEach((result, index) => {
                const score = players.get(result.playerId)?.score || 0;
                const podiumHeight = 70 + (score / maximum) * 145;
                const x = width / 2 + (index - (ordered.length - 1) / 2) * spacing;
                const podiumColour = result.rank === 1 ? 0xfacc15
                    : result.rank === 2 ? 0xcbd5e1
                        : result.rank === 3 ? 0xc08457 : 0x7c3aed;
                const isBiggestGainer = biggestGain > 0 && result.pointsAwarded === biggestGain;
                const block = this.add.rectangle(x, 660 - podiumHeight / 2, widthPerPodium, podiumHeight,
                    podiumColour, .92)
                    .setStrokeStyle(isBiggestGainer ? 6 : 4,
                        isBiggestGainer ? 0x67e8f9 : 0x24123f, 1);
                const oldRank = previousRanks.get(result.playerId) || result.rank;
                const movement = oldRank > result.rank ? ` ▲${oldRank - result.rank}`
                    : oldRank < result.rank ? ` ▼${result.rank - oldRank}` : "";
                const rank = this.add.text(x, 638, `#${result.rank}${movement}`, {
                    color: "#24123f", fontFamily: displayFont, fontSize: "28px", fontStyle: "bold"
                }).setOrigin(.5);
                items.push(block, rank);
                if (!this.controller.reducedMotion) {
                    block.setScale(1, .05);
                    block.setOrigin(.5, 1);
                    block.y = 660;
                    this.tweens.add({ targets: block, scaleY: 1, duration: 650, delay: index * 90, ease: "Back.easeOut" });
                    rank.setAlpha(0);
                    this.tweens.add({ targets: rank, alpha: 1, duration: 250, delay: 500 + index * 90 });
                }
            });
            this.podiumContainer = this.add.container(0, 0, items).setDepth(15);
            return true;
        }

        addFinalStatistics(statistics, items) {
            const visible = statistics.slice(0, 3);
            if (visible.length === 0) return;
            const cardWidth = 300;
            const gap = 18;
            visible.forEach((statistic, index) => {
                const x = width / 2 + (index - (visible.length - 1) / 2) * (cardWidth + gap);
                const y = 202;
                const shadow = this.add.graphics();
                shadow.fillStyle(0x090516, .32);
                shadow.fillRoundedRect(x - cardWidth / 2 + 6, y - 38 + 7, cardWidth, 76, 15);
                const panel = this.add.graphics();
                panel.fillStyle(0x24123f, .9);
                panel.fillRoundedRect(x - cardWidth / 2, y - 38, cardWidth, 76, 15);
                panel.lineStyle(3, 0x67e8f9, .75);
                panel.strokeRoundedRect(x - cardWidth / 2, y - 38, cardWidth, 76, 15);
                const label = this.add.text(x, y - 21, statistic.label || "BONUS AWARD", {
                    color: "#fde68a", fontFamily: displayFont, fontSize: "14px", fontStyle: "bold",
                    letterSpacing: 2, align: "center", wordWrap: { width: cardWidth - 24 }
                }).setOrigin(.5);
                const value = this.add.text(x, y + 13, statistic.value || "", {
                    color: "#ffffff", fontFamily: bodyFont, fontSize: "16px", fontStyle: "bold",
                    align: "center", wordWrap: { width: cardWidth - 24 }
                }).setOrigin(.5);
                items.push(shadow, panel, label, value);
                if (!this.controller.reducedMotion) {
                    [shadow, panel, label, value].forEach(item => item.setAlpha(0).setScale(.86));
                    this.tweens.add({
                        targets: [shadow, panel, label, value], alpha: 1, scale: 1,
                        duration: 360, delay: 250 + index * 170, ease: "Cubic.easeOut"
                    });
                }
            });
        }

        createAvatar(player) {
            const container = this.add.container(width / 2, height + 100);
            const cardShadow = this.add.rectangle(5, 7, 174, 174, 0x090516, .34)
                .setOrigin(.5);
            const card = this.add.rectangle(0, 0, 174, 174, 0x24123f, .94)
                .setStrokeStyle(3, 0x67e8f9, .72)
                .setOrigin(.5);
            const shadow = this.add.ellipse(0, 62, 112, 26, 0x090516, 0.32);
            const character = this.add.container(0, -58);
            const presence = this.add.text(0, -70, "", {
                color: "#fef3c7",
                fontFamily: displayFont,
                fontSize: "18px",
                fontStyle: "bold"
            }).setOrigin(0.5);
            const name = this.add.text(0, 47, player.displayName, {
                color: "#ffffff",
                fontFamily: bodyFont,
                fontSize: "22px",
                fontStyle: "bold",
                stroke: "#130828",
                strokeThickness: 5
            }).setOrigin(0.5);
            const score = this.add.text(0, 75, this.scoreLabel(player.score), {
                color: "#fde68a",
                fontFamily: displayFont,
                fontSize: "15px",
                fontStyle: "bold",
                stroke: "#130828",
                strokeThickness: 3
            }).setOrigin(0.5);
            const wins = this.add.text(0, 82, "", {
                color: "#f9a8d4",
                fontFamily: displayFont,
                fontSize: "11px",
                fontStyle: "bold",
                stroke: "#130828",
                strokeThickness: 2
            }).setOrigin(0.5);
            const activity = this.add.text(48, -142, "", {
                color: "#ffffff", backgroundColor: "#24123f", padding: { x: 10, y: 7 },
                fontFamily: displayFont, fontSize: "22px", fontStyle: "bold"
            }).setOrigin(.5);
            const removeDisc = this.add.circle(0, 0, 20, 0x28133f, .98)
                .setStrokeStyle(3, 0xff5aa5, 1);
            const removeLabel = this.add.text(0, -1, "×", {
                color: "#ffffff", fontFamily: bodyFont, fontSize: "25px", fontStyle: "bold"
            }).setOrigin(.5);
            const remove = this.add.container(78, -78, [removeDisc, removeLabel])
                .setSize(44, 44).setDepth(5).setVisible(false);
            if (this.controller.canManagePlayers) {
                remove.setInteractive({ useHandCursor: true });
                remove.on("pointerover", () => remove.setScale(1.12));
                remove.on("pointerout", () => remove.setScale(1));
                remove.on("pointerdown", () => {
                    if (this.controller.snapshot?.mode !== "Lobby") return;
                    remove.disableInteractive();
                    removeLabel.setText("…");
                    removeDisc.setFillStyle(0x4c1d5f, .98);
                    if (!this.controller.dotNetReference) {
                        removeLabel.setText("×");
                        removeDisc.setFillStyle(0x28133f, .98);
                        remove.setInteractive({ useHandCursor: true });
                        console.error("Player removal callback is unavailable.");
                        return;
                    }
                    this.controller.dotNetReference.invokeMethodAsync(
                        "RequestPlayerRemoval", player.playerId)
                        .catch(error => {
                            console.error("Unable to remove player.", error);
                            removeLabel.setText("×");
                            removeDisc.setFillStyle(0x28133f, .98);
                            remove.setInteractive({ useHandCursor: true });
                        });
                });
            }
            container.add([cardShadow, card, shadow, character, presence, name, score, wins, activity, remove]);
            container.setDepth(20);
            return {
                container, cardShadow, card, character, shadow, presence, name, score, wins, activity, remove,
                signature: null, mode: null, rig: null
            };
        }

        updateAvatar(avatar, player, mode) {
            const signature = JSON.stringify([player.character, mode]);
            if (signature !== avatar.signature) {
                this.drawCharacter(avatar, player.character, mode);
                avatar.signature = signature;
            }
            avatar.name.setText(player.displayName);
            avatar.name.setFontSize(Math.max(14, Math.min(22,
                Math.floor(250 / Math.max(10, player.displayName.length)))));
            const lobbyMode = this.controller.snapshot?.mode === "Lobby";
            avatar.name.setY(lobbyMode ? 39 : 47);
            avatar.score.setY(lobbyMode ? 62 : 75).setText(this.scoreLabel(player.score));
            avatar.wins
                .setText(`${Number(player.totalWins || 0).toLocaleString()} ${player.totalWins === 1 ? "WIN" : "WINS"}`)
                .setVisible(lobbyMode);
            const disconnected = player.status === "Disconnected";
            avatar.presence.setText(disconnected ? "OFFLINE" : "");
            const isThinking = player.activity === "Thinking";
            avatar.activity.setText(isThinking ? "…?" : "").setVisible(isThinking);
            avatar.container.setAlpha(disconnected ? 0.42 : 1);
            avatar.remove.setVisible(this.controller.canManagePlayers && this.controller.snapshot?.mode === "Lobby");
            if (!this.controller.reducedMotion && isThinking && !avatar.thinkingTween) {
                avatar.thinkingTween = this.tweens.add({
                    targets: avatar.activity, y: { from: -142, to: -154 },
                    duration: 650, yoyo: true, repeat: -1, ease: "Sine.easeInOut"
                });
            } else if (player.activity !== "Thinking" && avatar.thinkingTween) {
                avatar.thinkingTween.stop();
                avatar.thinkingTween = null;
                avatar.activity.setY(-142);
            }
        }

        drawCharacter(avatar, character, mode = "portrait") {
            avatar.rig ??= window.quizizzoCharacterRig.create(this, {
                container: avatar.character,
                atlasPrefix: "player-",
                armsInFront: true,
                handInset: 14
            });
            const variants = avatar.rig.render(character, mode);

            avatar.character.setScale(mode === "full" ? .31 : .4);
            avatar.character.setPosition(0, mode === "full" ? -160 : -54);
            avatar.shadow.setVisible(mode === "full");
            avatar.shadow.setY(mode === "full" ? 12 : 62);
            avatar.card.setVisible(mode === "portrait");
            avatar.cardShadow.setVisible(mode === "portrait");
            avatar.shadow.setScale(variants.bodyWidth, 1);
            avatar.mode = mode;
            avatar.pileAction = null;
        }

        layoutAvatars(snapshot, immediate, podiumChanged = true) {
            const players = snapshot.players || [];
            if (players.length === 0) {
                return;
            }

            const columns = Math.min(players.length, 6);
            const rows = Math.ceil(players.length / columns);
            const horizontalSpacing = Math.min(190, 1080 / columns);
            // The lobby QR occupies the centre of the upper canvas. Keep the
            // portrait row below it so the HTML overlay cannot mask faces.
            const compactShowdown = snapshot.gameKey === "animates"
                && ["ShowdownPlayback", "ShowdownVoting"].includes(snapshot.phase);
            const compactAnswerStage = snapshot.gameKey === "animates"
                && ["Choosing", "Results"].includes(snapshot.phase)
                && Boolean(snapshot.drawing?.animations?.length);
            const compactSlopStage = snapshot.gameKey === "slop-machine"
                && Boolean(snapshot.media?.items?.length);
            const baseY = snapshot.mode === "Lobby" ? (rows === 1 ? 575 : 555)
                : compactShowdown ? 618 : compactAnswerStage ? 620 : compactSlopStage ? 650 : 570;
            const rowSpacing = rows > 1 ? 165 : 0;
            const scale = compactShowdown ? 0.58 : compactAnswerStage ? .62 : compactSlopStage ? .5
                : players.length > 8 ? 0.62 : players.length > 6 ? 0.68 : 0.76;

            const podiumResults = snapshot.showRoundRanking
                ? [...(snapshot.results || [])].sort((left, right) => left.rank - right.rank)
                : null;
            const maximumScore = Math.max(1, ...players.map(player => player.score));
            const lastRank = podiumResults?.length
                ? Math.max(...podiumResults.map(result => result.rank))
                : null;
            const aniMatesFinal = snapshot.gameKey === "animates"
                && snapshot.phase === "FinalCelebration";
            players.forEach((player, index) => {
                const avatar = this.avatars.get(player.playerId);
                if (avatar) {
                    avatar.container.setVisible(!snapshot.presenterMessage);
                }
                if (podiumResults?.length) {
                    if (!podiumChanged) {
                        return;
                    }
                    const podiumIndex = podiumResults.findIndex(result => result.playerId === player.playerId);
                    if (podiumIndex < 0) {
                        avatar?.container.setVisible(false);
                        return;
                    }
                    const podiumResult = podiumResults[podiumIndex];
                    const spacing = Math.min(180, 1080 / podiumResults.length);
                    const x = width / 2 + (podiumIndex - (podiumResults.length - 1) / 2) * spacing;
                    const podiumHeight = 70 + (player.score / maximumScore) * 145;
                    const podiumTop = 660 - podiumHeight;
                    // The avatar container is the shoe baseline. Only the rig animates,
                    // so layout and expression tweens can never pull it inside a podium.
                    const y = podiumTop - 1;
                    if (avatar) {
                        this.tweens.killTweensOf(avatar.container);
                        avatar.container.setDepth(20);
                        if (immediate || this.controller.reducedMotion) {
                            avatar.container.setVisible(true).setAlpha(player.status === "Disconnected" ? .42 : 1)
                                .setPosition(x, y).setScale(.62);
                            if (this.controller.reducedMotion) {
                                avatar.rig?.stop();
                            } else {
                                avatar.rig?.play(
                                    podiumResult.rank === 1 ? "celebrate"
                                        : podiumResult.rank === lastRank && lastRank !== 1
                                            ? "cry" : "idle");
                                if (aniMatesFinal && podiumResult.rank === 1) {
                                    this.burst(x, y - 90, 42);
                                }
                            }
                        } else {
                            avatar.container.setVisible(true).setAlpha(0)
                                .setPosition(width / 2, 345).setScale(1.08);
                            this.tweens.add({
                                targets: avatar.container, x, y, scale: .62,
                                alpha: player.status === "Disconnected" ? .42 : 1,
                                delay: Math.max(0, podiumIndex) * 85,
                                duration: 850, ease: "Cubic.easeOut",
                                onComplete: () => {
                                    avatar.rig?.play(
                                        podiumResult.rank === 1 ? "celebrate"
                                            : podiumResult.rank === lastRank && lastRank !== 1
                                                ? "cry" : "idle");
                                    if (aniMatesFinal && podiumResult.rank === 1) {
                                        this.burst(x, y - 90, 42);
                                    }
                                }
                            });
                        }
                    }
                    return;
                }
                const row = Math.floor(index / columns);
                const itemsInRow = Math.min(columns, players.length - row * columns);
                const column = index % columns;
                const x = width / 2 + (column - (itemsInRow - 1) / 2) * horizontalSpacing;
                const y = baseY + (row - (rows - 1) / 2) * rowSpacing;
                if (!avatar) {
                    return;
                }

                if (immediate || this.controller.reducedMotion) {
                    avatar.container.setPosition(x, y).setScale(scale);
                } else {
                    this.tweens.add({
                        targets: avatar.container,
                        x,
                        y,
                        scale,
                        duration: 550,
                        ease: "Back.easeOut"
                    });
                }
            });
        }

        animateJoin(avatar) {
            if (this.controller.reducedMotion) {
                return;
            }
            avatar.container.setScale(0.05).setAlpha(1);
            this.tweens.add({
                targets: avatar.container,
                scale: 0.92,
                duration: 650,
                ease: "Back.easeOut"
            });
            this.burst(avatar.container.x, Math.min(avatar.container.y, 560), 28);
        }

        animateLeave(playerId, avatar) {
            if (this.controller.reducedMotion) {
                avatar.container.destroy(true);
                this.avatars.delete(playerId);
                return;
            }
            this.tweens.add({
                targets: avatar.container,
                y: height + 150,
                alpha: 0,
                duration: 450,
                ease: "Back.easeIn",
                onComplete: () => {
                    avatar.container.destroy(true);
                    this.avatars.delete(playerId);
                }
            });
        }

        animatePresence(avatar, status) {
            if (this.controller.reducedMotion || status === "Disconnected") {
                return;
            }
            this.tweens.add({
                targets: avatar.container,
                scaleX: avatar.container.scaleX * 1.18,
                scaleY: avatar.container.scaleY * 1.18,
                duration: 170,
                yoyo: true,
                ease: "Sine.easeOut"
            });
        }

        animateScore(avatar, difference) {
            if (this.controller.reducedMotion) {
                return;
            }
            avatar.score.setColor(difference >= 0 ? "#fff19a" : "#fca5a5");
            this.tweens.add({
                targets: avatar.score,
                scale: 1.65,
                duration: 220,
                yoyo: true,
                ease: "Back.easeOut",
                onComplete: () => avatar.score.setColor("#fde68a")
            });
            if (difference > 0) {
                this.burst(avatar.container.x, avatar.container.y - 25, 22);
            }
        }

        react(playerId, reaction) {
            const avatar = this.avatars.get(playerId);
            if (!avatar || avatar.mode !== "portrait") return;
            const symbols = {
                Kiss: "💋", Angry: "💢", Laugh: "😂", Wow: "❗", Poop: "💩",
                Fake: "FAKE", Unsubscribe: "UNSUBSCRIBE", Report: "REPORT THIS SLOP"
            };
            const direction = avatar.container.x > width / 2 ? -1 : 1;
            const symbol = this.add.text(
                avatar.container.x + direction * 105,
                avatar.container.y - 48,
                symbols[reaction] || "✨",
                { fontFamily: displayFont, fontSize: "46px" })
                .setOrigin(.5)
                .setDepth(200);
            const amount = reaction === "Angry" ? 9 : 4;
            this.tweens.add({
                targets: avatar.character,
                x: { from: -amount, to: amount },
                duration: reaction === "Angry" ? 55 : 120,
                yoyo: true,
                repeat: reaction === "Angry" ? 5 : 1,
                onComplete: () => avatar.character.setX(0)
            });
            this.tweens.add({
                targets: symbol,
                y: symbol.y - 42,
                alpha: 0,
                scale: 1.4,
                duration: 1050,
                ease: "Cubic.easeOut",
                onComplete: () => symbol.destroy()
            });
        }

        burst(x, y, quantity) {
            if (this.controller.reducedMotion || !this.controller.textureKey) {
                return;
            }
            const particles = this.add.particles(x, y, this.controller.textureKey, {
                speed: { min: 100, max: 290 },
                angle: { min: 205, max: 335 },
                gravityY: 380,
                lifespan: { min: 650, max: 1100 },
                scale: { start: 1, end: 0 },
                rotate: { min: 0, max: 360 },
                tint: [0xfacc15, 0x22d3ee, 0xff4d8d, 0xa78bfa, 0xffffff],
                emitting: false
            });
            particles.setDepth(50);
            particles.explode(quantity);
            this.time.delayedCall(1200, () => particles.destroy());
        }
    }

    async function start(key, elementId, snapshot, dotNetReference = null, canManagePlayers = false) {
        await stop(key);
        if (typeof Phaser === "undefined") {
            throw new Error("The locally hosted Phaser runtime is unavailable.");
        }

        const parent = document.getElementById(elementId);
        if (!parent) {
            throw new Error("The Phaser presentation container was not found.");
        }

        if (document.fonts) {
            await Promise.all([
                document.fonts.load('700 32px "Quizizzo Display"'),
                document.fonts.load('600 20px "Quizizzo Sans"')
            ]);
        }

        const resolution = renderResolution(parent);
        const controller = {
            key,
            snapshot: cloneSnapshot(snapshot),
            scene: null,
            textureKey: null,
            reducedMotion: window.matchMedia("(prefers-reduced-motion: reduce)").matches,
            game: null,
            renderResolution: resolution,
            resizeObserver: null,
            resizeHandler: null,
            resizeTimer: null,
            dotNetReference,
            canManagePlayers,
            audio: null,
            voiceAudio: null,
            replayRecorder: null,
            replayBlob: null
        };
        controller.ready = new Promise(resolve => { controller.readyResolve = resolve; });
        controller.audio = window.quizizzoPresentationAudio?.create((muted, blocked) => {
            controller.voiceAudio?.setMuted(muted || blocked);
            controller.dotNetReference?.invokeMethodAsync("AudioStateChanged", muted, blocked)
                .catch(() => { });
        }) || null;
        controller.voiceAudio = createVoiceChoonDisplayAudio();
        controller.audio?.update(controller.snapshot);
        controller.voiceAudio.update(controller.snapshot);
        const scene = new PartyPresentationScene(controller);
        controller.game = new Phaser.Game({
            type: Phaser.AUTO,
            parent: elementId,
            width: width * resolution,
            height: height * resolution,
            transparent: false,
            backgroundColor: "#101735",
            render: {
                antialias: true,
                pixelArt: false,
                roundPixels: true
            },
            scale: {
                mode: Phaser.Scale.ENVELOP,
                autoCenter: Phaser.Scale.CENTER_BOTH,
                width: width * resolution,
                height: height * resolution
            },
            scene
        });
        let readyTimeout;
        try {
            await Promise.race([
                controller.ready,
                new Promise((_, reject) => {
                    readyTimeout = window.setTimeout(
                        () => reject(new Error("The Phaser display did not initialise.")), 8000);
                })
            ]);
        } catch (error) {
            controller.audio?.destroy();
            controller.game?.destroy(true);
            throw error;
        } finally {
            window.clearTimeout(readyTimeout);
        }
        controller.resizeHandler = () => {
            const resolution = renderResolution(parent);
            if (Math.abs(controller.renderResolution - resolution) < .01) {
                return;
            }

            window.clearTimeout(controller.resizeTimer);
            controller.resizeTimer = window.setTimeout(() => {
                if (presentations.get(key) !== controller) {
                    return;
                }
                start(key, elementId, controller.snapshot,
                    controller.dotNetReference, controller.canManagePlayers).catch(error => {
                        console.error("Unable to resize the Quizizzo presentation.", error);
                        controller.dotNetReference?.invokeMethodAsync("PresentationFailed").catch(() => { });
                    });
            }, 150);
        };
        controller.resizeObserver = new ResizeObserver(controller.resizeHandler);
        controller.resizeObserver.observe(parent);
        window.addEventListener("resize", controller.resizeHandler, { passive: true });
        presentations.set(key, controller);
        if (controller.snapshot.gameKey === "voicechoon" && controller.snapshot.phase === "Playing") {
            startReplayRecording(controller);
        }
    }

    function update(key, snapshot) {
        const controller = presentations.get(key);
        if (!controller) {
            throw new Error("The Phaser presentation has not started.");
        }
        const previousPhase = controller.snapshot?.phase;
        controller.snapshot = cloneSnapshot(snapshot);
        controller.audio?.update(controller.snapshot);
        controller.voiceAudio?.update(controller.snapshot);
        controller.scene?.applySnapshot(controller.snapshot);
        if (controller.snapshot.gameKey === "voicechoon" && controller.snapshot.phase === "Playing") {
            startReplayRecording(controller);
        } else if (previousPhase === "Playing" && controller.snapshot.phase === "Results") {
            finishReplayRecording(controller);
        }
    }

    function react(key, playerId, reaction) {
        presentations.get(key)?.scene?.react(playerId, reaction);
    }

    function toggleAudio(key) {
        return presentations.get(key)?.audio?.toggle();
    }

    function configureHost(key, dotNetReference) {
        const controller = presentations.get(key);
        if (controller) {
            controller.dotNetReference = dotNetReference;
            controller.canManagePlayers = Boolean(dotNetReference);
        }
    }

    async function stop(key) {
        const controller = presentations.get(key);
        if (!controller) {
            return;
        }
        presentations.delete(key);
        window.clearTimeout(controller.resizeTimer);
        controller.resizeObserver?.disconnect();
        if (controller.resizeHandler) {
            window.removeEventListener("resize", controller.resizeHandler);
        }
        controller.audio?.destroy();
        finishReplayRecording(controller);
        controller.voiceAudio?.destroy();
        controller.game?.destroy(true);
    }

    async function shareReplay(key, caption) {
        const controller = presentations.get(key);
        if (!controller?.replayBlob || !navigator.share) return false;
        const extension = controller.replayBlob.type.includes("mp4") ? "mp4" : "webm";
        const file = new File([controller.replayBlob], `quizizzo-voicechoon.${extension}`,
            { type: controller.replayBlob.type });
        if (!navigator.canShare?.({ files: [file] })) return false;
        try {
            await navigator.share({ files: [file], title: "Our VoiceChoon performance", text: caption });
            return true;
        } catch (error) {
            if (error?.name !== "AbortError") console.warn("Unable to share the VoiceChoon replay.", error);
            return error?.name === "AbortError";
        }
    }

    function downloadReplay(key, fileName) {
        const blob = presentations.get(key)?.replayBlob;
        if (!blob) return;
        const extension = blob.type.includes("mp4") ? "mp4" : "webm";
        const link = document.createElement("a");
        link.href = URL.createObjectURL(blob);
        link.download = `${fileName || "quizizzo-voicechoon"}.${extension}`;
        link.click();
        window.setTimeout(() => URL.revokeObjectURL(link.href), 1000);
    }

    return { start, update, react, toggleAudio, configureHost, stop, shareReplay, downloadReplay };
})();
