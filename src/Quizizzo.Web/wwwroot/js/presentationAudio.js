window.quizizzoPresentationAudio = (() => {
    const mutedStorageKey = "quizizzo.display.audio-muted";
    const seenCueStorageKey = "quizizzo.display.audio-cues";
    const countdownWindowSeconds = 20;
    const configuration = Object.freeze({
        musicVolume: .28,
        countdownVolume: .38,
        victoryVolume: .48,
        duckedMusicMultiplier: .4,
        normalFadeInMilliseconds: 400,
        normalFadeOutMilliseconds: 400,
        crossfadeMilliseconds: 600,
        scoreboardToVictoryMilliseconds: 300
    });
    const slopRoot = "/media/audio/games/slop-machine";
    const assets = Object.freeze({
        lobby: `${slopRoot}/slop-lobby.mp3`,
        game: "/media/audio/quiz-show-sparkle.774e332653a6.mp3",
        countdown: "/media/audio/countdown-to-zero.fd84e59f102d.mp3",
        slopWriting: `${slopRoot}/slop-writing.mp3`,
        slopCountdown: `${slopRoot}/slop-countdown.mp3`,
        slopSpinner: `${slopRoot}/slop-spinner.mp3`,
        slopVoting: `${slopRoot}/slop-voting.mp3`,
        slopTelephone: `${slopRoot}/slop-telephone.mp3`,
        slopComments: `${slopRoot}/slop-comments.mp3`,
        slopScoreboard: `${slopRoot}/slop-scoreboard.mp3`,
        slopFinal: `${slopRoot}/slop-final.mp3`,
        slopHumanVictory: `${slopRoot}/slop-human-victory.mp3`,
        slopMachineVictory: `${slopRoot}/slop-machine-victory.mp3`
    });
    const tracks = Object.freeze({
        lobby: { source: assets.lobby, loop: true, volume: configuration.musicVolume },
        game: { source: assets.game, loop: true, volume: configuration.musicVolume },
        countdown: { source: assets.countdown, loop: false, volume: configuration.countdownVolume },
        slopWriting: { source: assets.slopWriting, loop: true, volume: configuration.musicVolume },
        slopCountdown: { source: assets.slopCountdown, loop: false, volume: configuration.countdownVolume },
        slopSpinner: { source: assets.slopSpinner, loop: false, volume: configuration.musicVolume },
        slopVoting: { source: assets.slopVoting, loop: true, volume: configuration.musicVolume },
        slopTelephone: { source: assets.slopTelephone, loop: true, volume: configuration.musicVolume },
        slopComments: { source: assets.slopComments, loop: true, volume: configuration.musicVolume },
        slopScoreboard: { source: assets.slopScoreboard, loop: true, volume: configuration.musicVolume },
        slopFinal: { source: assets.slopFinal, loop: true, volume: configuration.musicVolume },
        slopHumanVictory: {
            source: assets.slopHumanVictory, loop: false, volume: configuration.victoryVolume, cue: true
        },
        slopMachineVictory: {
            source: assets.slopMachineVictory, loop: false, volume: configuration.victoryVolume, cue: true
        }
    });

    function storedMuted() {
        try {
            return window.localStorage.getItem(mutedStorageKey) === "true";
        } catch {
            return false;
        }
    }

    function storeMuted(muted) {
        try {
            window.localStorage.setItem(mutedStorageKey, String(muted));
        } catch {
            // Audio still works when browser storage is unavailable.
        }
    }

    function storedCueKeys() {
        try {
            const parsed = JSON.parse(window.sessionStorage?.getItem(seenCueStorageKey) || "[]");
            return new Set(Array.isArray(parsed) ? parsed.filter(value => typeof value === "string") : []);
        } catch {
            return new Set();
        }
    }

    function storeCueKeys(keys) {
        try {
            window.sessionStorage?.setItem(seenCueStorageKey, JSON.stringify([...keys].slice(-24)));
        } catch {
            // Replaying a cue after a storage failure is preferable to breaking audio.
        }
    }

    function audioElement(source = "") {
        const element = source ? new Audio(source) : new Audio();
        element.preload = "auto";
        element.volume = 0;
        element.loop = false;
        return element;
    }

    function validDeadline(snapshot) {
        if (!snapshot?.phaseEndsAtUtc) return null;
        const time = Date.parse(snapshot.phaseEndsAtUtc);
        return Number.isFinite(time) ? time : null;
    }

    function countdownState(snapshot, now) {
        const deadline = validDeadline(snapshot);
        if (!deadline) return null;
        const isAniMates = snapshot.gameKey === "animates" && snapshot.phase === "Drawing";
        const isSlopWriting = snapshot.gameKey === "slop-machine" && [
            "FreshSlopWriting", "AlgorithmRouletteWriting", "TelephoneWriting",
            "CommentsWriting", "FinalWriting"
        ].includes(snapshot.phase);
        if (!isAniMates && !isSlopWriting) return null;
        const remaining = Math.max(0, (deadline - now) / 1000);
        return {
            deadline,
            remaining,
            key: `${snapshot.gameKey}|${snapshot.phase}|${snapshot.phaseEndsAtUtc}`,
            trackKey: isSlopWriting ? "slopCountdown" : "countdown"
        };
    }

    function slopBackground(snapshot) {
        const phase = snapshot.phase;
        if (phase === "GameIntro") return { trackKey: "lobby", sessionKey: "lobby" };
        if (["FreshSlopWriting", "AlgorithmRouletteWriting"].includes(phase)) {
            return {
                trackKey: "slopWriting",
                sessionKey: `${phase}|${snapshot.phaseEndsAtUtc || "untimed"}`,
                reset: true
            };
        }
        if (phase === "AlgorithmRouletteSpinning") {
            return { trackKey: "slopSpinner", sessionKey: phase, reset: true };
        }
        if (phase === "FreshSlopVoting") {
            return {
                trackKey: "slopVoting",
                sessionKey: "fresh-slop-voting",
                reset: true
            };
        }
        if (phase === "AlgorithmRouletteVoting") {
            return {
                trackKey: "slopVoting",
                sessionKey: "roulette-voting",
                reset: true
            };
        }
        if ([
            "ThumbnailTelephoneIntro", "TelephoneWriting", "TelephoneMatching",
            "TelephoneVoting", "TelephoneResults"
        ].includes(phase)) {
            return { trackKey: "slopTelephone", sessionKey: "slop-telephone", resume: true };
        }
        if ([
            "CommentsIntro", "CommentsWriting", "CommentsVoting", "CommentsResults"
        ].includes(phase)) {
            return { trackKey: "slopComments", sessionKey: "slop-comments", resume: true };
        }
        if (["ScoreReview1", "ScoreReview2", "ScoreReview3", "ScoreReview4", "FinalScoreReview"]
            .includes(phase)) {
            return { trackKey: "slopScoreboard", sessionKey: phase, reset: true };
        }
        if (["FinalIntro", "FinalWriting", "FinalVoting", "FinalMachineGuess", "FinalResults"]
            .includes(phase)) {
            return {
                trackKey: "slopFinal",
                sessionKey: "slop-final",
                reset: phase === "FinalIntro",
                resume: phase !== "FinalIntro"
            };
        }
        return null;
    }

    function backgroundState(snapshot, now = Date.now()) {
        if (!snapshot) return null;
        const countdown = countdownState(snapshot, now);
        if (countdown?.remaining > 0 && countdown.remaining <= countdownWindowSeconds) {
            return {
                trackKey: countdown.trackKey,
                sessionKey: countdown.key,
                reset: true,
                offset: countdownWindowSeconds - countdown.remaining,
                countdown
            };
        }
        if (snapshot.mode === "Lobby") return { trackKey: "lobby", sessionKey: "lobby" };
        if (snapshot.mode !== "Game") return null;
        if (snapshot.gameKey === "slop-machine") return slopBackground(snapshot);
        return { trackKey: "game", sessionKey: `game|${snapshot.gameKey || "unknown"}` };
    }

    function cueState(snapshot) {
        if (snapshot?.gameKey !== "slop-machine") return null;
        const room = snapshot.roomCode || "unpaired";
        const game = snapshot.gameInstanceId || `revision-${snapshot.revision}`;
        if (snapshot.phase === "FinalMachineGuess" &&
            snapshot.phaseMessage?.startsWith("THE MACHINE WON", 0)) {
            return {
                trackKey: "slopMachineVictory",
                cueKey: `${room}|${game}|machine-victory`
            };
        }
        if (snapshot.phase === "WinnerCelebration") {
            return {
                trackKey: "slopHumanVictory",
                cueKey: `${room}|${game}|human-victory`
            };
        }
        return null;
    }

    class PresentationAudioController {
        constructor(stateChanged, options = {}) {
            this.stateChanged = stateChanged;
            this.now = options.now || (() => Date.now());
            this.fadeInMilliseconds = options.fadeInMilliseconds ?? configuration.normalFadeInMilliseconds;
            this.crossfadeMilliseconds = options.crossfadeMilliseconds ?? configuration.crossfadeMilliseconds;
            this.fadeOutMilliseconds = options.fadeOutMilliseconds ?? configuration.normalFadeOutMilliseconds;
            this.scoreboardToVictoryMilliseconds = options.scoreboardToVictoryMilliseconds ??
                configuration.scoreboardToVictoryMilliseconds;
            this.backgrounds = [audioElement(), audioElement()];
            this.cue = audioElement();
            this.preloader = audioElement();
            this.activeBackgroundIndex = 0;
            this.currentBackground = null;
            this.currentCue = null;
            this.positions = new Map();
            this.unavailable = new Set();
            this.warnedUnavailable = new Set();
            this.seenCues = storedCueKeys();
            this.snapshot = null;
            this.muted = storedMuted();
            this.blocked = false;
            this.ducked = false;
            this.destroyed = false;
            this.countdownTimer = null;
            this.fadeTimers = new Set();
            this.stateSignature = null;
            this.operation = 0;
            [...this.backgrounds, this.cue, this.preloader].forEach(element => {
                element.addEventListener?.("error", () => this.audioFailed(element));
            });
            this.cue.addEventListener?.("ended", () => this.cueEnded());
            this.gestureHandler = event => {
                if (event.target?.closest?.(".display-audio-toggle")) return;
                if (this.blocked && !this.muted) this.enable().catch(() => { });
            };
            document.addEventListener("pointerdown", this.gestureHandler, { passive: true });
            this.notify();
        }

        get activeBackground() {
            return this.backgrounds[this.activeBackgroundIndex];
        }

        get activeTrackKey() {
            return this.currentCue?.trackKey || this.currentBackground?.trackKey || null;
        }

        update(snapshot) {
            if (this.snapshot?.mode === "Game" && snapshot?.mode === "Game" &&
                this.snapshot.roomCode === snapshot.roomCode &&
                this.snapshot.gameInstanceId === snapshot.gameInstanceId &&
                Number.isFinite(this.snapshot.revision) && Number.isFinite(snapshot.revision) &&
                snapshot.revision < this.snapshot.revision) {
                return;
            }
            this.snapshot = snapshot;
            this.apply().catch(() => { });
        }

        async toggle() {
            if (this.muted || this.blocked) {
                await this.enable();
                return;
            }
            this.muted = true;
            this.blocked = false;
            storeMuted(true);
            this.operation++;
            this.pauseAll();
            this.notify();
        }

        async enable() {
            this.muted = false;
            this.blocked = false;
            storeMuted(false);
            this.notify();
            await this.apply();
        }

        async apply() {
            const operation = ++this.operation;
            window.clearTimeout(this.countdownTimer);
            this.countdownTimer = null;
            if (this.destroyed || this.muted || !this.snapshot) {
                this.pauseAll();
                this.notify();
                return;
            }

            const cue = cueState(this.snapshot);
            if (this.currentCue && this.currentCue.cueKey !== cue?.cueKey) this.stopCue();
            if (cue && !this.seenCues.has(cue.cueKey)) {
                if (await this.playCue(cue, operation)) return;
            }
            if (this.currentCue) return;

            const desired = backgroundState(this.snapshot, this.now());
            await this.transitionBackground(desired, operation);
            if (operation !== this.operation || this.destroyed) return;
            this.scheduleCountdown(this.snapshot);
            this.preloadLikelyNext(this.snapshot);
            this.notify();
        }

        scheduleCountdown(snapshot) {
            const countdown = countdownState(snapshot, this.now());
            if (!countdown || countdown.remaining <= countdownWindowSeconds) return;
            this.countdownTimer = window.setTimeout(() => {
                if (!this.muted && this.snapshot) this.apply().catch(() => { });
            }, (countdown.remaining - countdownWindowSeconds) * 1000);
        }

        async transitionBackground(desired, operation, fadeMilliseconds = this.crossfadeMilliseconds) {
            if (desired && this.unavailable.has(desired.trackKey)) desired = null;
            const descriptor = desired ? tracks[desired.trackKey] : null;
            if (!descriptor) {
                await this.fadeAndPause(this.activeBackground, this.fadeOutMilliseconds, operation);
                if (operation === this.operation) this.currentBackground = null;
                return;
            }

            if (this.currentBackground?.trackKey === desired.trackKey) {
                const active = this.activeBackground;
                const inactive = this.backgrounds[1 - this.activeBackgroundIndex];
                if (!inactive.paused) {
                    inactive.volume = 0;
                    inactive.pause();
                }
                if (this.currentBackground.sessionKey !== desired.sessionKey && desired.reset) {
                    active.currentTime = Math.max(0, desired.offset || 0);
                }
                this.currentBackground = { ...desired, source: descriptor.source };
                active.loop = descriptor.loop;
                active.volume = this.targetVolume(descriptor);
                if (active.paused !== false) await this.tryPlay(active, operation, desired.trackKey);
                return;
            }

            const previous = this.activeBackground;
            const hadBackground = Boolean(this.currentBackground);
            if (this.currentBackground?.source && Number.isFinite(previous.currentTime)) {
                this.positions.set(this.currentBackground.source, previous.currentTime);
            }
            const nextIndex = 1 - this.activeBackgroundIndex;
            const next = this.backgrounds[nextIndex];
            next.pause();
            if (next.src !== descriptor.source && !next.src?.endsWith(descriptor.source)) next.src = descriptor.source;
            next._quizizzoTrackKey = desired.trackKey;
            next.loop = descriptor.loop;
            next.currentTime = Math.max(0, desired.offset ??
                (desired.resume ? this.positions.get(descriptor.source) || 0 : 0));
            next.volume = 0;
            const played = await this.tryPlay(next, operation, desired.trackKey);
            if (!played || operation !== this.operation) return;
            this.activeBackgroundIndex = nextIndex;
            this.currentBackground = { ...desired, source: descriptor.source };
            const transitionMilliseconds = hadBackground ? fadeMilliseconds : this.fadeInMilliseconds;
            await Promise.all([
                this.fade(previous, previous.volume, 0, transitionMilliseconds, operation, true),
                this.fade(next, 0, this.targetVolume(descriptor), transitionMilliseconds, operation, false)
            ]);
        }

        async playCue(cue, operation) {
            const descriptor = tracks[cue.trackKey];
            if (!descriptor || this.unavailable.has(cue.trackKey)) return false;
            const background = this.activeBackground;
            if (this.currentBackground?.source && Number.isFinite(background.currentTime)) {
                this.positions.set(this.currentBackground.source, background.currentTime);
            }
            const fade = cue.trackKey === "slopHumanVictory"
                ? this.scoreboardToVictoryMilliseconds
                : this.fadeOutMilliseconds;
            await this.fadeAndPause(background, fade, operation);
            if (operation !== this.operation) return false;
            this.currentBackground = null;
            this.cue.pause();
            if (this.cue.src !== descriptor.source && !this.cue.src?.endsWith(descriptor.source)) {
                this.cue.src = descriptor.source;
            }
            this.cue._quizizzoTrackKey = cue.trackKey;
            this.cue.currentTime = 0;
            this.cue.loop = false;
            this.cue.volume = descriptor.volume;
            const played = await this.tryPlay(this.cue, operation, cue.trackKey);
            if (!played || operation !== this.operation) return false;
            this.currentCue = cue;
            this.seenCues.add(cue.cueKey);
            storeCueKeys(this.seenCues);
            this.notify();
            return true;
        }

        cueEnded() {
            if (!this.currentCue) return;
            this.currentCue = null;
            if (!this.destroyed && !this.muted) this.apply().catch(() => { });
        }

        stopCue() {
            this.cue.pause();
            this.cue.currentTime = 0;
            this.currentCue = null;
        }

        async tryPlay(element, operation, trackKey) {
            try {
                await element.play();
                if (operation !== this.operation || this.destroyed) {
                    element.pause();
                    return false;
                }
                this.blocked = false;
                return true;
            } catch (error) {
                if (operation !== this.operation || this.destroyed || error?.name === "AbortError") return false;
                if (!this.unavailable.has(trackKey)) this.blocked = true;
                this.pauseAll();
                this.notify();
                return false;
            }
        }

        targetVolume(descriptor) {
            return descriptor.volume * (this.ducked ? configuration.duckedMusicMultiplier : 1);
        }

        duck() {
            this.ducked = true;
            if (this.currentBackground) {
                this.activeBackground.volume = this.targetVolume(tracks[this.currentBackground.trackKey]);
            }
        }

        restoreVolume() {
            this.ducked = false;
            if (this.currentBackground) {
                this.activeBackground.volume = this.targetVolume(tracks[this.currentBackground.trackKey]);
            }
        }

        fadeAndPause(element, duration, operation) {
            return this.fade(element, element.volume, 0, duration, operation, true);
        }

        fade(element, from, to, duration, operation, pauseAtEnd) {
            if (duration <= 0 || from === to) {
                element.volume = to;
                if (pauseAtEnd) element.pause();
                return Promise.resolve();
            }
            return new Promise(resolve => {
                const started = this.now();
                const timer = window.setInterval(() => {
                    if (operation !== this.operation || this.destroyed) {
                        window.clearInterval(timer);
                        this.fadeTimers.delete(timer);
                        resolve();
                        return;
                    }
                    const progress = Math.min(1, (this.now() - started) / duration);
                    element.volume = from + (to - from) * progress;
                    if (progress < 1) return;
                    window.clearInterval(timer);
                    this.fadeTimers.delete(timer);
                    if (pauseAtEnd) element.pause();
                    resolve();
                }, 40);
                this.fadeTimers.add(timer);
            });
        }

        preloadLikelyNext(snapshot) {
            let trackKey = null;
            const countdown = countdownState(snapshot, this.now());
            if (countdown?.remaining > countdownWindowSeconds) trackKey = countdown.trackKey;
            else if (snapshot.gameKey === "slop-machine" && snapshot.phase === "FinalScoreReview") {
                trackKey = "slopHumanVictory";
            }
            const descriptor = trackKey ? tracks[trackKey] : null;
            if (!descriptor || this.unavailable.has(trackKey) || this.preloader.src?.endsWith(descriptor.source)) return;
            this.preloader._quizizzoTrackKey = trackKey;
            this.preloader.src = descriptor.source;
            this.preloader.load();
        }

        audioFailed(element) {
            const trackKey = element._quizizzoTrackKey;
            if (!trackKey || this.unavailable.has(trackKey)) return;
            this.unavailable.add(trackKey);
            if (!this.warnedUnavailable.has(trackKey)) {
                this.warnedUnavailable.add(trackKey);
                console.warn(`Quizizzo display audio unavailable: ${trackKey} (${tracks[trackKey]?.source || "unknown"})`);
            }
            element.pause();
            if (this.currentCue?.trackKey === trackKey) this.currentCue = null;
            if (this.currentBackground?.trackKey === trackKey) this.currentBackground = null;
            if (!this.destroyed && !this.muted) this.apply().catch(() => { });
        }

        pauseAll() {
            this.backgrounds.forEach(element => element.pause());
            this.cue.pause();
        }

        notify() {
            const signature = `${this.muted}|${this.blocked}`;
            if (signature === this.stateSignature) return;
            this.stateSignature = signature;
            this.stateChanged?.(this.muted, this.blocked);
        }

        destroy() {
            this.destroyed = true;
            this.operation++;
            window.clearTimeout(this.countdownTimer);
            this.fadeTimers.forEach(timer => window.clearInterval(timer));
            this.fadeTimers.clear();
            document.removeEventListener("pointerdown", this.gestureHandler);
            this.pauseAll();
            [...this.backgrounds, this.cue, this.preloader].forEach(element => {
                element.removeAttribute("src");
                element.load();
            });
        }
    }

    return {
        assets,
        configuration,
        tracks,
        backgroundState,
        cueState,
        create: (stateChanged, options) => new PresentationAudioController(stateChanged, options)
    };
})();
