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

    const characterAssetRoot = "/assets/kenney-presenter/spritesheets/";
    const characterSheets = ["face", "hair", "pants", "shirts", "shoes", "skin"];

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
            this.drawingContainer = null;
            this.drawingTimer = null;
            this.drawingSignature = null;
        }

        preload() {
            characterSheets.forEach(sheet => this.load.atlasXML(
                `player-${sheet}`,
                `${characterAssetRoot}sheet_${sheet}.png`,
                `${characterAssetRoot}sheet_${sheet}.xml`));
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
            const characterMode = (snapshot.results || []).some(result => result.rank != null)
                ? "full"
                : "portrait";
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
                this.updateAvatar(avatar, player, characterMode);
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
            let animationIndex = 0;
            let frameIndex = 0;
            const panel = this.add.rectangle(0, 0, 500, 390, 0xffffff, 0.97)
                .setStrokeStyle(8, 0x24123f, 1);
            const frame = this.add.image(0, -20, `drawing-${drawing.animations[0].frameUrls[0].split("/").pop()}`)
                .setDisplaySize(330, 330);
            const caption = this.add.text(0, 172, "", {
                color: "#24123f",
                fontFamily: "Arial, sans-serif",
                fontSize: "24px",
                fontStyle: "bold",
                align: "center",
                wordWrap: { width: 450 }
            }).setOrigin(0.5);
            this.drawingContainer = this.add.container(width / 2, 275, [panel, frame, caption]).setDepth(12);

            const show = () => {
                const animation = drawing.animations[animationIndex];
                const url = animation.frameUrls[frameIndex];
                frame.setTexture(`drawing-${url.split("/").pop()}`);
                const reveal = drawing.mode === "Reveal" && animation.creatorName
                    ? `${animation.prompt}\n${animation.creatorName} — ${animation.votes} vote(s)`
                    : animation.prompt;
                caption.setText(reveal);
                frameIndex += 1;
                if (frameIndex >= animation.frameUrls.length) {
                    frameIndex = 0;
                    animationIndex = (animationIndex + 1) % drawing.animations.length;
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
            const character = this.add.container(0, -58);
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
            container.add([shadow, character, presence, name, score]);
            container.setDepth(20);
            return { container, character, shadow, presence, name, score, signature: null, mode: null };
        }

        updateAvatar(avatar, player, mode) {
            const signature = JSON.stringify([player.character, mode]);
            if (signature !== avatar.signature) {
                this.drawCharacter(avatar, player.character, mode);
                avatar.signature = signature;
            }
            avatar.name.setText(player.displayName);
            avatar.score.setText(`${player.score.toLocaleString()} pts`);
            const disconnected = player.status === "Disconnected";
            avatar.presence.setText(disconnected ? "OFFLINE" : "");
            avatar.container.setAlpha(disconnected ? 0.42 : 1);
        }

        drawCharacter(avatar, character, mode = "portrait") {
            avatar.character.removeAll(true);
            const variants = this.characterFrames(character);
            const add = (x, y, atlas, frame, originX = .5, originY = .5) => {
                const image = this.add.image(x, y, `player-${atlas}`, frame).setOrigin(originX, originY);
                avatar.character.add(image);
                return image;
            };
            if (mode === "full") {
                add(0, 168, "skin", `tint${variants.skin}_neck.png`, .5, 0).setScale(.72, 1);
                add(-58, 218, "shirts", `${variants.shirt}Arm_long.png`, .69, .18).setFlipX(true);
                add(58, 218, "shirts", `${variants.shirt}Arm_long.png`, .31, .18);
                add(-166, 301, "skin", `tint${variants.skin}_hand.png`, .5, .12);
                add(166, 301, "skin", `tint${variants.skin}_hand.png`, .5, .12);
                add(-95.5, 341, "skin", `tint${variants.skin}_leg.png`, 0, 0).setFlipX(true);
                add(95.5, 341, "skin", `tint${variants.skin}_leg.png`, 1, 0);
                add(-95.5, 341, "pants", `${variants.pants}_${variants.trouserLength}.png`, 0, 0).setFlipX(true);
                add(95.5, 341, "pants", `${variants.pants}_${variants.trouserLength}.png`, 1, 0);
                add(-66, 505, "shoes", variants.shoe).setFlipX(true).setScale(.86);
                add(66, 505, "shoes", variants.shoe).setScale(.86);
                add(0, 200, "shirts", `${variants.shirt}Shirt${variants.presentation === "Woman" ? 4 : 1}.png`, .5, 0);
                add(0, 341, "pants", `${variants.pants}1.png`, .5, 0);
            }

            add(0, 0, "skin", `tint${variants.skin}_head.png`, .5, 0);
            add(0, -25, "hair", variants.hair, .5, 0);
            add(-27, 75, "face", variants.eye);
            add(27, 75, "face", variants.eye);
            add(-28, 55, "face", variants.brow);
            add(28, 55, "face", variants.brow).setFlipX(true);
            add(0, 98, "face", `tint${variants.skin}Nose1.png`);
            add(0, 132, "face", variants.mouth);

            avatar.character.setScale(mode === "full" ? .31 : .5);
            avatar.character.setPosition(0, mode === "full" ? -160 : -78);
            avatar.shadow.setVisible(mode === "full");
            avatar.mode = mode;
        }

        characterFrames(character) {
            const body = { Bean: 1, Blob: 3, Round: 5, Square: 7 };
            const skin = [1, 3, 5, 7].includes(character.skinTone)
                ? character.skinTone
                : body[character.bodyType] || 1;
            const presentation = character.presentation ||
                (["Blob", "Square"].includes(character.bodyType) ? "Woman" : "Man");
            const legacyHair = {
                Bright: `blonde${presentation}1.png`,
                Sleepy: `brown1${presentation}${presentation === "Man" ? 5 : 3}.png`,
                Starry: `red${presentation}${presentation === "Man" ? 1 : 4}.png`,
                Googly: `black${presentation}${presentation === "Man" ? 2 : 1}.png`
            }[character.eyes];
            const hairPrefix = { Brown: "brown1", Black: "black", Blonde: "blonde", Red: "red" }[character.hairColour];
            const hair = hairPrefix
                ? `${hairPrefix}${presentation}${presentation === "Man" ? 1 : 3}.png`
                : legacyHair;
            const eye = character.eyes === "Sleepy" ? "eyeBrown_large.png" : "eyeBlue_large.png";
            const browPrefix = character.eyes === "Googly" ? "black" : character.eyes === "Starry" ? "red" : character.eyes === "Bright" ? "blonde" : "brown1";
            const mouth = { Smile: "mouth_happy.png", Grin: "mouth_teethUpper.png", Surprised: "mouth_oh.png", Tongue: "mouth_glad.png" }[character.mouth] || "mouth_happy.png";
            const colourValue = colour(character.primaryColour);
            const paletteIndex = colourValue % 4;
            const shirt = { Navy: "navy", Blue: "blue", Green: "green", Red: "red" }[character.shirtColour];
            const pants = { Navy: "pantsNavy", Blue: "pantsBlue1", Green: "pantsGreen", Tan: "pantsTan" }[character.trouserColour];
            const shoe = { Brown: "brownShoe1.png", Black: "blackShoe1.png", Blue: "blueShoe1.png", Red: "redShoe1.png" }[character.shoeColour];
            const trouserLength = { FullLength: "long", Cropped: "short", Shorts: "shorter" }[character.trouserLength];
            return {
                skin,
                presentation,
                hair,
                eye,
                brow: `${browPrefix}Brow1.png`,
                mouth,
                shirt: shirt || ["navy", "blue", "green", "red"][paletteIndex],
                pants: pants || ["pantsNavy", "pantsBlue1", "pantsGreen", "pantsTan"][paletteIndex],
                trouserLength: trouserLength || "long",
                shoe: shoe || ["brownShoe1.png", "blackShoe1.png", "blueShoe1.png", "redShoe1.png"][paletteIndex]
            };
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
            // The lobby QR occupies the centre of the upper canvas. Keep the
            // portrait row below it so the HTML overlay cannot mask faces.
            const baseY = snapshot.mode === "Lobby" ? (rows === 1 ? 575 : 555) : 548;
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

        react(playerId, reaction) {
            const avatar = this.avatars.get(playerId);
            if (!avatar || avatar.mode !== "portrait") return;
            const symbols = { Kiss: "💋", Angry: "💢", Laugh: "😂", Wow: "❗" };
            const symbol = this.add.text(
                avatar.container.x + 42,
                avatar.container.y - 150,
                symbols[reaction] || "✨",
                { fontFamily: "Arial, sans-serif", fontSize: "46px" })
                .setOrigin(.5)
                .setDepth(80);
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
                y: symbol.y - 70,
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

    async function start(key, elementId, snapshot) {
        await stop(key);
        if (typeof Phaser === "undefined") {
            throw new Error("The locally hosted Phaser runtime is unavailable.");
        }

        const parent = document.getElementById(elementId);
        if (!parent) {
            throw new Error("The Phaser presentation container was not found.");
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
            resizeTimer: null
        };
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
                mode: Phaser.Scale.FIT,
                autoCenter: Phaser.Scale.CENTER_BOTH,
                width: width * resolution,
                height: height * resolution
            },
            scene
        });
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
                start(key, elementId, controller.snapshot).catch(error =>
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
        controller.scene?.applySnapshot(controller.snapshot);
    }

    function react(key, playerId, reaction) {
        presentations.get(key)?.scene?.react(playerId, reaction);
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
        controller.game?.destroy(true);
    }

    return { start, update, react, stop };
})();
