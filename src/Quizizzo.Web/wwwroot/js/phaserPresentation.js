window.quizizzoPresentation = (() => {
    const presentations = new Map();
    const width = 1280;
    const height = 720;

    function colour(value, fallback = 0x7c3aed) {
        if (typeof value !== "string" || !/^#[0-9a-f]{6}$/i.test(value)) {
            return fallback;
        }
        return Number.parseInt(value.slice(1), 16);
    }

    function cloneSnapshot(snapshot) {
        return JSON.parse(JSON.stringify(snapshot));
    }

    function playerMap(snapshot) {
        return new Map((snapshot?.players || []).map(player => [player.playerId, player]));
    }

    class PartyPresentationScene extends Phaser.Scene {
        constructor(controller) {
            super({ key: `party-presentation-${controller.key}` });
            this.controller = controller;
            this.avatars = new Map();
            this.previous = null;
            this.background = null;
        }

        create() {
            this.createBackground();
            this.createParticleTexture();
            this.controller.scene = this;
            this.applySnapshot(this.controller.snapshot, true);
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

        drawBackground(gameKey) {
            const palette = gameKey === "estimate"
                ? [0x160b32, 0x39156b, 0x7132a8]
                : [0x101735, 0x272a68, 0x513487];
            this.background.clear();
            this.background.fillGradientStyle(
                palette[1], palette[2], palette[0], palette[1], 1);
            this.background.fillRect(0, 0, width, height);
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

        applySnapshot(snapshot, initial = false) {
            if (!snapshot) {
                return;
            }

            const previousPlayers = playerMap(this.previous);
            const currentIds = new Set((snapshot.players || []).map(player => player.playerId));
            this.drawBackground(snapshot.gameKey);

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
                this.updateAvatar(avatar, player);
                if (previousPlayer && previousPlayer.score !== player.score) {
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

            this.layoutAvatars(snapshot, initial);
            if (!initial && this.isNewResult(snapshot)) {
                this.animateResults(snapshot.results || []);
            }
            this.previous = cloneSnapshot(snapshot);
        }

        isNewResult(snapshot) {
            if (!snapshot.results?.length) {
                return false;
            }
            if (!this.previous?.results?.length) {
                return true;
            }
            return JSON.stringify(snapshot.results) !== JSON.stringify(this.previous.results);
        }

        createAvatar(player) {
            const container = this.add.container(width / 2, height + 100);
            const shadow = this.add.ellipse(0, 62, 112, 26, 0x090516, 0.32);
            const body = this.add.graphics();
            const face = this.add.graphics();
            const accessory = this.add.graphics();
            const presence = this.add.text(0, -84, "", {
                color: "#fef3c7",
                fontFamily: "Arial, sans-serif",
                fontSize: "18px",
                fontStyle: "bold"
            }).setOrigin(0.5);
            const name = this.add.text(0, 86, player.displayName, {
                color: "#ffffff",
                fontFamily: "Arial, sans-serif",
                fontSize: "22px",
                fontStyle: "bold",
                stroke: "#130828",
                strokeThickness: 5
            }).setOrigin(0.5);
            const score = this.add.text(0, 114, `${player.score.toLocaleString()} pts`, {
                color: "#fde68a",
                fontFamily: "Arial, sans-serif",
                fontSize: "18px",
                fontStyle: "bold",
                stroke: "#130828",
                strokeThickness: 4
            }).setOrigin(0.5);
            container.add([shadow, body, face, accessory, presence, name, score]);
            container.setDepth(20);
            return { container, body, face, accessory, presence, name, score, signature: null };
        }

        updateAvatar(avatar, player) {
            const signature = JSON.stringify(player.character);
            if (signature !== avatar.signature) {
                this.drawCharacter(avatar, player.character);
                avatar.signature = signature;
            }
            avatar.name.setText(player.displayName);
            avatar.score.setText(`${player.score.toLocaleString()} pts`);
            const disconnected = player.status === "Disconnected";
            avatar.presence.setText(disconnected ? "OFFLINE" : "");
            avatar.container.setAlpha(disconnected ? 0.42 : 1);
        }

        drawCharacter(avatar, character) {
            const bodyColour = colour(character.primaryColour);
            const body = avatar.body;
            const face = avatar.face;
            const accessory = avatar.accessory;
            body.clear();
            face.clear();
            accessory.clear();

            body.lineStyle(7, 0x170a2e, 0.55);
            body.fillStyle(bodyColour, 1);
            switch (character.bodyType) {
                case "Square":
                    body.fillRoundedRect(-52, -52, 104, 112, 20);
                    body.strokeRoundedRect(-52, -52, 104, 112, 20);
                    break;
                case "Bean":
                    body.fillEllipse(0, 6, 98, 132);
                    body.strokeEllipse(0, 6, 98, 132);
                    break;
                case "Blob":
                    body.fillPoints([
                        new Phaser.Geom.Point(-52, 48),
                        new Phaser.Geom.Point(-59, -7),
                        new Phaser.Geom.Point(-35, -55),
                        new Phaser.Geom.Point(10, -67),
                        new Phaser.Geom.Point(51, -38),
                        new Phaser.Geom.Point(58, 20),
                        new Phaser.Geom.Point(34, 60),
                        new Phaser.Geom.Point(-18, 66)
                    ], true);
                    body.strokePoints([
                        new Phaser.Geom.Point(-52, 48),
                        new Phaser.Geom.Point(-59, -7),
                        new Phaser.Geom.Point(-35, -55),
                        new Phaser.Geom.Point(10, -67),
                        new Phaser.Geom.Point(51, -38),
                        new Phaser.Geom.Point(58, 20),
                        new Phaser.Geom.Point(34, 60),
                        new Phaser.Geom.Point(-18, 66)
                    ], true);
                    break;
                default:
                    body.fillCircle(0, 3, 58);
                    body.strokeCircle(0, 3, 58);
                    break;
            }

            this.drawEyes(face, character.eyes);
            this.drawMouth(face, character.mouth);
            this.drawAccessory(accessory, character.accessory);
        }

        drawEyes(graphic, eyes) {
            if (eyes === "Sleepy") {
                graphic.lineStyle(5, 0x190d27, 1);
                graphic.lineBetween(-32, -10, -10, -7);
                graphic.lineBetween(10, -7, 32, -10);
                return;
            }

            if (eyes === "Starry") {
                graphic.fillStyle(0xfff5a5, 1);
                graphic.fillStar(-22, -11, 5, 5, 12);
                graphic.fillStar(22, -11, 5, 5, 12);
                return;
            }

            graphic.fillStyle(0xffffff, 1);
            graphic.fillCircle(-22, -10, eyes === "Googly" ? 16 : 13);
            graphic.fillCircle(22, -10, eyes === "Googly" ? 12 : 13);
            graphic.fillStyle(0x190d27, 1);
            graphic.fillCircle(eyes === "Googly" ? -17 : -22, -8, 6);
            graphic.fillCircle(eyes === "Googly" ? 18 : 22, -12, 6);
        }

        drawMouth(graphic, mouth) {
            graphic.lineStyle(5, 0x190d27, 1);
            if (mouth === "Grin") {
                graphic.fillStyle(0xffffff, 1);
                graphic.fillRoundedRect(-23, 20, 46, 20, 8);
                graphic.strokeRoundedRect(-23, 20, 46, 20, 8);
            } else if (mouth === "Surprised") {
                graphic.fillStyle(0x190d27, 1);
                graphic.fillCircle(0, 29, 12);
            } else if (mouth === "Tongue") {
                graphic.fillStyle(0x190d27, 1);
                graphic.fillRoundedRect(-19, 18, 38, 27, 12);
                graphic.fillStyle(0xff6b9d, 1);
                graphic.fillEllipse(0, 41, 24, 15);
            } else {
                graphic.beginPath();
                graphic.arc(0, 19, 23, 0.15, Math.PI - 0.15, false);
                graphic.strokePath();
            }
        }

        drawAccessory(graphic, accessory) {
            graphic.lineStyle(4, 0x190d27, 0.7);
            if (accessory === "Crown") {
                graphic.fillStyle(0xfacc15, 1);
                graphic.fillPoints([
                    new Phaser.Geom.Point(-36, -52),
                    new Phaser.Geom.Point(-28, -82),
                    new Phaser.Geom.Point(-8, -61),
                    new Phaser.Geom.Point(5, -88),
                    new Phaser.Geom.Point(20, -60),
                    new Phaser.Geom.Point(39, -80),
                    new Phaser.Geom.Point(34, -49)
                ], true);
                graphic.strokePoints([
                    new Phaser.Geom.Point(-36, -52),
                    new Phaser.Geom.Point(-28, -82),
                    new Phaser.Geom.Point(-8, -61),
                    new Phaser.Geom.Point(5, -88),
                    new Phaser.Geom.Point(20, -60),
                    new Phaser.Geom.Point(39, -80),
                    new Phaser.Geom.Point(34, -49)
                ], true);
            } else if (accessory === "BowTie") {
                graphic.fillStyle(0xff4d8d, 1);
                graphic.fillTriangle(-5, 48, -36, 34, -34, 60);
                graphic.fillTriangle(5, 48, 36, 34, 34, 60);
                graphic.fillCircle(0, 48, 9);
            } else if (accessory === "PartyHat") {
                graphic.fillStyle(0x22d3ee, 1);
                graphic.fillTriangle(-31, -52, 13, -102, 34, -50);
                graphic.fillStyle(0xffd43b, 1);
                graphic.fillCircle(14, -103, 9);
            } else if (accessory === "Glasses") {
                graphic.lineStyle(6, 0x24123f, 1);
                graphic.strokeCircle(-23, -10, 20);
                graphic.strokeCircle(23, -10, 20);
                graphic.lineBetween(-3, -10, 3, -10);
            }
        }

        layoutAvatars(snapshot, immediate) {
            const players = snapshot.players || [];
            if (players.length === 0) {
                return;
            }

            const columns = Math.min(players.length, 6);
            const rows = Math.ceil(players.length / columns);
            const horizontalSpacing = Math.min(190, 1080 / columns);
            const baseY = snapshot.mode === "Lobby" ? 505 : 548;
            const rowSpacing = rows > 1 ? 165 : 0;
            const scale = players.length > 8 ? 0.72 : players.length > 6 ? 0.8 : 0.92;

            players.forEach((player, index) => {
                const row = Math.floor(index / columns);
                const itemsInRow = Math.min(columns, players.length - row * columns);
                const column = index % columns;
                const x = width / 2 + (column - (itemsInRow - 1) / 2) * horizontalSpacing;
                const y = baseY + (row - (rows - 1) / 2) * rowSpacing;
                const avatar = this.avatars.get(player.playerId);
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

        animateResults(results) {
            if (this.controller.reducedMotion) {
                return;
            }
            const winners = results.filter(result => result.rank === 1);
            winners.forEach(result => {
                const avatar = this.avatars.get(result.playerId);
                if (!avatar) {
                    return;
                }
                this.tweens.add({
                    targets: avatar.container,
                    y: avatar.container.y - 42,
                    duration: 260,
                    yoyo: true,
                    repeat: 2,
                    ease: "Quad.easeOut"
                });
                this.burst(avatar.container.x, avatar.container.y - 50, 54);
            });
            this.cameras.main.flash(260, 255, 235, 135, false);
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

    async function start(key, elementId, snapshot) {
        await stop(key);
        if (typeof Phaser === "undefined") {
            throw new Error("The locally hosted Phaser runtime is unavailable.");
        }

        const parent = document.getElementById(elementId);
        if (!parent) {
            throw new Error("The Phaser presentation container was not found.");
        }

        const controller = {
            key,
            snapshot: cloneSnapshot(snapshot),
            scene: null,
            textureKey: null,
            reducedMotion: window.matchMedia("(prefers-reduced-motion: reduce)").matches,
            game: null
        };
        const scene = new PartyPresentationScene(controller);
        controller.game = new Phaser.Game({
            type: Phaser.AUTO,
            parent: elementId,
            width,
            height,
            transparent: false,
            backgroundColor: "#101735",
            render: {
                antialias: true,
                pixelArt: false,
                roundPixels: true
            },
            scale: {
                mode: Phaser.Scale.FIT,
                autoCenter: Phaser.Scale.CENTER_BOTH,
                width,
                height
            },
            scene
        });
        presentations.set(key, controller);
    }

    function update(key, snapshot) {
        const controller = presentations.get(key);
        if (!controller) {
            throw new Error("The Phaser presentation has not started.");
        }
        controller.snapshot = cloneSnapshot(snapshot);
        controller.scene?.applySnapshot(controller.snapshot);
    }

    async function stop(key) {
        const controller = presentations.get(key);
        if (!controller) {
            return;
        }
        presentations.delete(key);
        controller.game?.destroy(true);
    }

    return { start, update, stop };
})();
