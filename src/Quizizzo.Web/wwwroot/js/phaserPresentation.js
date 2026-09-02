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
            const results = gameKey === "animates" && ["Results", "ShowdownResults"].includes(phase);
            const palette = briefing
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
            if (briefing) {
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

        applySnapshot(snapshot, initial = false) {
            if (!snapshot) {
                return;
            }

            const previousPlayers = playerMap(this.previous);
            const currentIds = new Set((snapshot.players || []).map(player => player.playerId));
            const showRoundRanking = Boolean(snapshot.showRoundRanking && snapshot.results?.length);
            const characterMode = showRoundRanking ? "full" : "portrait";
            this.drawBackground(snapshot.gameKey, snapshot.phase);
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
                this.tweens.add({ targets: this.presenterContainer, x: width / 2,
                    duration: 700, ease: "Back.easeOut" });
                host.rig.play(isBriefing ? "talk" : "idle");
                if (isBriefing) {
                    this.tweens.add({ targets: [bubble, speech], scale: { from: .985, to: 1.015 },
                        duration: 1150, yoyo: true, repeat: -1, ease: "Sine.easeInOut" });
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
                this.tweens.add({ targets: this.tutorialContainer, alpha: 1, scale: 1,
                    duration: 600, delay: 420, ease: "Back.easeOut" });
                const firstCard = items[2];
                this.tweens.add({ targets: firstCard, scale: 1.08, duration: 450,
                    yoyo: true, repeat: -1, ease: "Sine.easeInOut" });
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
            const targetScale = hasSideCards ? .72 : .78;
            const targetX = hasSideCards ? 350 : width / 2;
            const shadow = this.add.rectangle(18, 20, 530, 430, 0x090516, .4);
            const panel = this.add.rectangle(0, 0, 530, 430, 0xfffbeb, 0.99)
                .setStrokeStyle(9, 0x24123f, 1);
            const inner = this.add.rectangle(0, -18, 370, 370, 0xffffff, 1)
                .setStrokeStyle(3, 0xa78bfa, .8);
            const tapeLeft = this.add.rectangle(-205, -190, 90, 28, 0xfde68a, .78).setAngle(-8);
            const tapeRight = this.add.rectangle(205, -190, 90, 28, 0x67e8f9, .72).setAngle(8);
            const frame = this.add.image(0, -20, `drawing-${drawing.animations[0].frameUrls[0].split("/").pop()}`)
                .setDisplaySize(350, 350);
            const caption = this.add.text(0, 172, "", {
                color: "#24123f",
                fontFamily: displayFont,
                fontSize: "25px",
                fontStyle: "bold",
                align: "center",
                wordWrap: { width: 450 }
            }).setOrigin(0.5);
            const frameDots = Array.from({ length: drawing.animations[0].frameUrls.length }, (_, index) =>
                this.add.circle((index - (drawing.animations[0].frameUrls.length - 1) / 2) * 18, 202, 5,
                    index === 0 ? 0xdb2777 : 0xc4b5fd, 1));
            this.drawingContainer = this.add.container(targetX, 310,
                [shadow, panel, inner, tapeLeft, tapeRight, frame, caption, ...frameDots])
                .setDepth(12).setScale(targetScale);
            if (!this.controller.reducedMotion) {
                this.drawingContainer.setScale(targetScale * .82).setAlpha(0).setAngle(-2);
                this.tweens.add({ targets: this.drawingContainer, scale: targetScale, alpha: 1, angle: 0,
                    duration: 520, ease: "Back.easeOut" });
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
                    delay: Math.max(100, drawing.frameDurationMilliseconds || 150),
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
                this.tweens.add({ targets: this.drawingContainer, alpha: 1, scale: 1,
                    duration: 420, ease: "Back.easeOut" });
            }
            const show = () => cards.forEach(card => {
                const url = card.animation.frameUrls[card.frameIndex];
                card.frame.setTexture(`drawing-${url.split("/").pop()}`);
                card.frameIndex = (card.frameIndex + 1) % card.animation.frameUrls.length;
            });
            show();
            if (!this.controller.reducedMotion) {
                this.drawingTimer = this.time.addEvent({
                    delay: Math.max(100, drawing.frameDurationMilliseconds || 150),
                    loop: true,
                    callback: show
                });
            }
        }

        animatePhaseTransition(phase) {
            if (this.controller.reducedMotion) return;
            this.phaseChrome?.destroy(true);
            const labels = {
                Drawing: "DRAW!", Guessing: "WHAT IS IT?", Choosing: "PICK AN ANSWER",
                Results: "REVEAL!", ShowdownPlayback: "SHOWDOWN", ShowdownVoting: "VOTE NOW",
                ShowdownResults: "THE WINNER"
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
            this.tweens.add({ targets: this.phaseChrome, x: width / 2, duration: 360, ease: "Back.easeOut",
                hold: 520, yoyo: true, onComplete: () => {
                    this.phaseChrome?.destroy(true);
                    this.phaseChrome = null;
                } });
            this.cameras.main.shake(120, .004);
        }

        startShowdownReveal(drawing) {
            const animations = drawing.animations || [];
            if (animations.length === 0) return;
            const grid = this.showdownGrid(animations);
            const frameSize = Math.max(78, Math.min(grid.cardWidth - 24, grid.cardHeight - 66));
            const items = [];
            animations.forEach((animation, index) => {
                const row = Math.floor(index / grid.columns);
                const column = index % grid.columns;
                const x = (column - (Math.min(grid.columns, animations.length - row * grid.columns) - 1) / 2)
                    * (grid.cardWidth + grid.gapX);
                const y = row * (grid.cardHeight + grid.gapY);
                const shadow = this.add.rectangle(x + 7, y + 9, grid.cardWidth, grid.cardHeight, 0x090516, .38);
                const panel = this.add.rectangle(x, y, grid.cardWidth, grid.cardHeight, 0xfffbeb, .99)
                    .setStrokeStyle(animation.rank === 1 ? 9 : 5,
                        animation.rank === 1 ? 0xfacc15 : 0x24123f, 1);
                const frameUrl = animation.frameUrls[0];
                const frame = this.add.image(x, y - 17, `drawing-${frameUrl.split("/").pop()}`)
                    .setDisplaySize(frameSize, frameSize);
                const caption = this.add.text(x, y + grid.cardHeight / 2 - 17,
                    `${animation.prompt} — ${animation.creatorName || "?"}`, {
                        color: "#24123f", fontFamily: displayFont, fontSize: "19px",
                        fontStyle: "bold", align: "center", wordWrap: { width: grid.cardWidth - 20 }
                    }).setOrigin(.5);
                const badge = this.add.text(x - grid.cardWidth / 2 + 10, y - grid.cardHeight / 2 + 10,
                    animation.prompt, {
                    color: "#ffffff", backgroundColor: animation.rank === 1 ? "#db2777" : "#312e81",
                    padding: { x: 9, y: 5 }, fontFamily: displayFont, fontSize: "18px", fontStyle: "bold"
                }).setOrigin(0, 0).setAngle(-2);
                items.push(shadow, panel, frame, caption, badge);
                if (animation.rank === 1 && !this.controller.reducedMotion) {
                    this.tweens.add({ targets: [shadow, panel, frame, caption, badge], scale: 1.12,
                        duration: 450, yoyo: true, repeat: 1, ease: "Back.easeOut" });
                    this.burst(width / 2 + x, 138 + grid.cardHeight / 2 + y, 60);
                }
            });
            this.drawingContainer = this.add.container(width / 2, 138 + grid.cardHeight / 2, items).setDepth(12);
        }

        startRoundRanking(snapshot, initial) {
            const players = playerMap(snapshot);
            const signature = JSON.stringify({
                revision: snapshot.revision,
                results: snapshot.results,
                scores: snapshot.results.map(result => players.get(result.playerId)?.score || 0)
            });
            if (signature === this.roundRankingSignature) return;

            this.stopRoundRanking();
            this.roundRankingSignature = signature;
            const previousPlayers = playerMap(this.previous);
            this.roundRankingStartScores = new Map(snapshot.players.map(player => [
                player.playerId,
                Math.min(player.score, previousPlayers.get(player.playerId)?.score ?? player.score)
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
            this.applyPresenter("That's another round over — let's see how the scores look!");
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
                avatar.score.setText(`${start.toLocaleString()} pts`);
                const timer = this.time.delayedCall(delay, () => {
                    if (this.roundRankingSignature !== signature) return;
                    const counter = { value: start };
                    const tween = this.tweens.add({
                        targets: counter,
                        value: player.score,
                        duration,
                        ease: "Cubic.easeOut",
                        onUpdate: () => avatar.score.setText(
                            `${Math.round(counter.value).toLocaleString()} pts`),
                        onComplete: () => {
                            avatar.score.setText(`${player.score.toLocaleString()} pts`);
                            this.tweens.add({ targets: avatar.score, scale: 1.45,
                                duration: 150, yoyo: true, ease: "Back.easeOut" });
                            if (difference > 0) this.burst(avatar.container.x, avatar.container.y + 25, 18);
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
            const items = [
                this.add.text(width / 2, 56, "ROUND COMPLETE", {
                    color: "#fde68a", fontFamily: displayFont, fontSize: "24px", fontStyle: "bold",
                    letterSpacing: 5
                }).setOrigin(.5),
                this.add.text(width / 2, 96, "CURRENT STANDINGS", {
                    color: "#ffffff", fontFamily: displayFont, fontSize: "46px", fontStyle: "bold",
                    stroke: "#24123f", strokeThickness: 7
                }).setOrigin(.5)
            ];
            ordered.forEach((result, index) => {
                const score = players.get(result.playerId)?.score || 0;
                const podiumHeight = 70 + (score / maximum) * 145;
                const x = width / 2 + (index - (ordered.length - 1) / 2) * spacing;
                const podiumColour = result.rank === 1 ? 0xfacc15
                    : result.rank === 2 ? 0xcbd5e1
                        : result.rank === 3 ? 0xc08457 : 0x7c3aed;
                const block = this.add.rectangle(x, 660 - podiumHeight / 2, widthPerPodium, podiumHeight,
                    podiumColour, .92)
                    .setStrokeStyle(4, 0x24123f, 1);
                const rank = this.add.text(x, 638, `#${result.rank}`, {
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
            const score = this.add.text(0, 75, `${player.score.toLocaleString()} pts`, {
                color: "#fde68a",
                fontFamily: displayFont,
                fontSize: "15px",
                fontStyle: "bold",
                stroke: "#130828",
                strokeThickness: 3
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
            container.add([cardShadow, card, shadow, character, presence, name, score, activity, remove]);
            container.setDepth(20);
            return { container, cardShadow, card, character, shadow, presence, name, score, activity, remove,
                signature: null, mode: null, rig: null };
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
            avatar.score.setText(`${player.score.toLocaleString()} pts`);
            const disconnected = player.status === "Disconnected";
            avatar.presence.setText(disconnected ? "OFFLINE" : "");
            const isThinking = player.activity === "Thinking";
            avatar.activity.setText(isThinking ? "…?" : "").setVisible(isThinking);
            avatar.container.setAlpha(disconnected ? 0.42 : 1);
            avatar.remove.setVisible(this.controller.canManagePlayers && this.controller.snapshot?.mode === "Lobby");
            if (!this.controller.reducedMotion && isThinking && !avatar.thinkingTween) {
                avatar.thinkingTween = this.tweens.add({ targets: avatar.activity, y: { from: -142, to: -154 },
                    duration: 650, yoyo: true, repeat: -1, ease: "Sine.easeInOut" });
            } else if (player.activity !== "Thinking" && avatar.thinkingTween) {
                avatar.thinkingTween.stop();
                avatar.thinkingTween = null;
                avatar.activity.setY(-142);
            }
        }

        drawCharacter(avatar, character, mode = "portrait") {
            avatar.rig ??= window.quizizzoCharacterRig.create(this, {
                container: avatar.character,
                atlasPrefix: "player-"
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
            const baseY = snapshot.mode === "Lobby" ? (rows === 1 ? 575 : 555) : compactShowdown ? 618 : 570;
            const rowSpacing = rows > 1 ? 165 : 0;
            const scale = compactShowdown ? 0.58 : players.length > 8 ? 0.62 : players.length > 6 ? 0.68 : 0.76;

            const podiumResults = snapshot.showRoundRanking
                ? [...(snapshot.results || [])].sort((left, right) => left.rank - right.rank)
                : null;
            const maximumScore = Math.max(1, ...players.map(player => player.score));
            const lastRank = podiumResults?.length
                ? Math.max(...podiumResults.map(result => result.rank))
                : null;
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
                            avatar.rig?.stop();
                        } else {
                            avatar.container.setVisible(true).setAlpha(0)
                                .setPosition(width / 2, 345).setScale(1.08);
                            this.tweens.add({
                                targets: avatar.container, x, y, scale: .62,
                                alpha: player.status === "Disconnected" ? .42 : 1,
                                delay: Math.max(0, podiumIndex) * 85,
                                duration: 850, ease: "Cubic.easeOut",
                                onComplete: () => avatar.rig?.play(
                                    podiumResult.rank === 1 ? "laugh"
                                        : podiumResult.rank === lastRank && lastRank !== 1
                                            ? "cry" : "idle")
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
            const symbols = { Kiss: "💋", Angry: "💢", Laugh: "😂", Wow: "❗", Poop: "💩" };
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
            audio: null
        };
        controller.audio = window.quizizzoPresentationAudio?.create((muted, blocked) => {
            controller.dotNetReference?.invokeMethodAsync("AudioStateChanged", muted, blocked)
                .catch(() => { });
        }) || null;
        controller.audio?.update(controller.snapshot);
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
        parent.closest(".display-stage")?.classList.add("phaser-enhanced");
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
                    controller.dotNetReference, controller.canManagePlayers).catch(error =>
                    console.error("Unable to resize the Quizizzo presentation.", error));
            }, 150);
        };
        controller.resizeObserver = new ResizeObserver(controller.resizeHandler);
        controller.resizeObserver.observe(parent);
        window.addEventListener("resize", controller.resizeHandler, { passive: true });
        presentations.set(key, controller);
    }

    function update(key, snapshot) {
        const controller = presentations.get(key);
        if (!controller) {
            throw new Error("The Phaser presentation has not started.");
        }
        controller.snapshot = cloneSnapshot(snapshot);
        controller.audio?.update(controller.snapshot);
        controller.scene?.applySnapshot(controller.snapshot);
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
        controller.game?.destroy(true);
    }

    return { start, update, react, toggleAudio, configureHost, stop };
})();
