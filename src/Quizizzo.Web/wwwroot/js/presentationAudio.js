window.quizizzoPresentationAudio = (() => {
    const mutedStorageKey = "quizizzo.display.audio-muted";
    const countdownWindowSeconds = 20;
    const assets = Object.freeze({
        lobby: "/media/audio/quiz-show-groove.d6618b4f874d.mp3",
        game: "/media/audio/quiz-show-sparkle.774e332653a6.mp3",
        countdown: "/media/audio/countdown-to-zero.fd84e59f102d.mp3"
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

    function audioElement(source, volume, loop) {
        const element = source ? new Audio(source) : new Audio();
        element.preload = "auto";
        element.volume = volume;
        element.loop = loop;
        return element;
    }

    class PresentationAudioController {
        constructor(stateChanged) {
            this.stateChanged = stateChanged;
            this.background = audioElement("", .28, true);
            this.countdown = audioElement(assets.countdown, .72, false);
            this.snapshot = null;
            this.muted = storedMuted();
            this.blocked = false;
            this.destroyed = false;
            this.countdownTimer = null;
            this.activeCountdownKey = null;
            this.backgroundSource = null;
            this.stateSignature = null;
            this.operation = 0;
            this.gestureHandler = event => {
                if (event.target?.closest?.(".display-audio-toggle")) return;
                if (this.blocked && !this.muted) this.enable().catch(() => { });
            };
            document.addEventListener("pointerdown", this.gestureHandler, { passive: true });
            this.notify();
        }

        update(snapshot) {
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

            const source = this.snapshot.mode === "Lobby"
                ? assets.lobby
                : this.snapshot.mode === "Game" ? assets.game : null;
            if (!source) {
                this.activeCountdownKey = null;
                this.pauseAll();
                this.notify();
                return;
            }

            const deadline = this.drawingDeadline(this.snapshot);
            if (deadline) {
                const remaining = (deadline.time - Date.now()) / 1000;
                if (remaining > 0 && remaining <= countdownWindowSeconds) {
                    await this.startCountdown(deadline.key, remaining, operation);
                    return;
                }

                if (remaining > countdownWindowSeconds) {
                    this.countdownTimer = window.setTimeout(() => {
                        if (!this.muted && this.snapshot) {
                            this.startCountdown(deadline.key, countdownWindowSeconds, ++this.operation)
                                .catch(() => { });
                        }
                    }, (remaining - countdownWindowSeconds) * 1000);
                }
            }

            this.stopCountdown();
            await this.playBackground(source, operation);
        }

        drawingDeadline(snapshot) {
            if (snapshot.gameKey !== "animates" || snapshot.phase !== "Drawing" ||
                !snapshot.phaseEndsAtUtc) {
                return null;
            }

            const time = Date.parse(snapshot.phaseEndsAtUtc);
            return Number.isFinite(time)
                ? { time, key: `${snapshot.phase}|${snapshot.phaseEndsAtUtc}` }
                : null;
        }

        async playBackground(source, operation) {
            if (this.backgroundSource !== source) {
                this.background.pause();
                this.backgroundSource = source;
                this.background.src = source;
                this.background.currentTime = 0;
            }
            await this.tryPlay(this.background, operation);
        }

        async startCountdown(key, remaining, operation) {
            if (this.destroyed || this.muted) return;
            this.background.pause();
            if (this.activeCountdownKey !== key) {
                this.countdown.pause();
                this.activeCountdownKey = key;
                this.countdown.currentTime = Math.max(
                    0, Math.min(countdownWindowSeconds - remaining, this.countdown.duration || countdownWindowSeconds));
            }
            await this.tryPlay(this.countdown, operation);
        }

        async tryPlay(element, operation) {
            try {
                await element.play();
                if (operation !== this.operation || this.destroyed) return;
                this.blocked = false;
            } catch (error) {
                if (operation !== this.operation || this.destroyed || error?.name === "AbortError") return;
                this.blocked = true;
                this.pauseAll();
            }
            this.notify();
        }

        stopCountdown() {
            this.countdown.pause();
            this.countdown.currentTime = 0;
            this.activeCountdownKey = null;
        }

        pauseAll() {
            this.background.pause();
            this.countdown.pause();
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
            document.removeEventListener("pointerdown", this.gestureHandler);
            this.pauseAll();
            this.background.removeAttribute("src");
            this.countdown.removeAttribute("src");
            this.background.load();
            this.countdown.load();
        }
    }

    return {
        assets,
        create: stateChanged => new PresentationAudioController(stateChanged)
    };
})();
