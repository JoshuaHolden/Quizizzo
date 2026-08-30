(() => {
    const labels = {
        idle: ["Natural idle", "Ready to play?"], wave: ["Friendly wave", "Hey, party people!"],
        talk: ["Talking loop", "The answers are coming in…"], laugh: ["Laughing reaction", "Ha! That is ridiculous!"],
        celebrate: ["Winner celebration", "And we have a winner!"], think: ["Thinking pose", "Hmm… what would I choose?"],
        fart: ["Cheeky interruption", "Pfffft! Excuse me!"]
    };
    const assetRoot = "/assets/kenney-presenter/";
    const pick = values => values[Math.floor(Math.random() * values.length)];
    const skins = [{ name: "warm", suffix: "" }, { name: "golden", suffix: "-3" }, { name: "brown", suffix: "-5" }, { name: "deep", suffix: "-7" }];

    class PresenterScene extends Phaser.Scene {
        constructor(host) { super("PresenterLab"); this.host = host; this.activeEffects = []; }
        preload() {
            const files = ["head", "neck", "hand", "nose", "hair", "hair-black-man", "hair-blonde-man", "hair-red-man",
                "hair-brown-woman", "hair-black-woman", "hair-blonde-woman", "hair-red-woman", "shirt", "arm", "pants", "leg", "shoe-smart",
                "eye", "brow", "mouth-happy", "mouth-glad", "mouth-oh", "mouth-teeth"];
            ["-3", "-5", "-7"].forEach(suffix => ["head", "neck", "hand", "nose"].forEach(part => files.push(part + suffix)));
            ["blue", "green", "red"].forEach(colour => files.push("shirt-" + colour, "arm-" + colour));
            files.push("shoe-rounded-black", "shoe-rounded-blue", "shoe-rounded-red");
            files.push("shoe-smart-black", "shoe-smart-blue", "shoe-smart-red");
            ["blue", "green", "tan"].forEach(colour => files.push("pants-" + colour, "leg-" + colour));
            ["navy", "blue", "green", "tan"].forEach(colour => files.push(`leg-${colour}-three-quarter-v2`, `leg-${colour}-shorts-v2`));
            files.push("skin-leg", "skin-leg-3", "skin-leg-5", "skin-leg-7");
            ["navy", "blue", "green", "red"].forEach(colour => {
                for (let variant = 1; variant <= 8; variant++) files.push(`top-${colour}-${variant}`);
            });
            files.forEach(file => this.load.image(file, assetRoot + file + ".png"));
        }
        create() {
            this.rig = this.add.container(360, 18).setScale(.95);
            this.rig.add(this.add.ellipse(0, 523, 250, 30, 0x02091f, .42));
            // Sink each leg beneath the hip piece so its curved outside corner
            // meets the belt edge instead of starting as a visible square step.
            this.leftSkinLeg = this.part(-95.5, 341, "skin-leg", 0, 0).setFlipX(true);
            this.rightSkinLeg = this.part(95.5, 341, "skin-leg", 1, 0);
            this.leftLeg = this.part(-95.5, 341, "leg", 0, 0).setFlipX(true);
            this.rightLeg = this.part(95.5, 341, "leg", 1, 0);
            this.leftShoe = this.part(-66, 505, "shoe-smart").setFlipX(true).setScale(.86);
            this.rightShoe = this.part(66, 505, "shoe-smart").setScale(.86);
            this.leftArm = this.makeArm(-58, 218, true);
            this.rightArm = this.makeArm(58, 218, false);
            this.neckBacking = this.add.graphics(); this.rig.add(this.neckBacking);
            this.neck = this.part(0, 168, "neck", .5, 0).setScale(.72, 1);
            this.shirt = this.part(0, 200, "shirt", .5, 0);
            this.pants = this.part(0, 341, "pants", .5, 0);
            this.head = this.add.container(0, 35); this.rig.add(this.head);
            this.headShape = this.facePart(0, 0, "head", .5, 0);
            this.hair = this.facePart(0, -25, "hair", .5, 0);
            this.eyeL = this.facePart(-27, 75, "eye"); this.eyeR = this.facePart(27, 75, "eye");
            this.browL = this.facePart(-28, 55, "brow"); this.browR = this.facePart(28, 55, "brow").setFlipX(true);
            this.nose = this.facePart(0, 98, "nose");
            this.mouths = ["mouth-happy", "mouth-glad", "mouth-oh", "mouth-teeth"].map((key, index) => this.facePart(0, 132, key).setVisible(index === 0));
            this.randomise(); this.play("idle"); this.host.presenterScene = this;
        }
        part(x, y, key, originX = .5, originY = .5) { const image = this.add.image(x, y, key).setOrigin(originX, originY); this.rig.add(image); return image; }
        facePart(x, y, key, originX = .5, originY = .5) { const image = this.add.image(x, y, key).setOrigin(originX, originY); this.head.add(image); return image; }
        makeArm(x, y, left) {
            const arm = this.add.container(x, y);
            const sleeve = this.add.image(0, 0, "arm").setOrigin(left ? .69 : .31, .18).setFlipX(left);
            const hand = this.add.image(left ? -108 : 108, 83, "hand").setOrigin(.5, .12);
            arm.add([sleeve, hand]); arm.sleeve = sleeve; arm.hand = hand; this.rig.add(arm); return arm;
        }
        clearMotion() {
            this.tweens.killAll(); this.activeEffects.forEach(effect => effect.remove()); this.activeEffects = [];
            this.rig.setPosition(360, 18).setAngle(0).setScale(.95); this.head.setAngle(0);
            [this.leftArm, this.rightArm].forEach(arm => arm.setAngle(0).setScale(1));
            this.leftArm.hand.setAngle(0); this.rightArm.hand.setAngle(0); this.setMouth(0); this.eyeL.setScale(1); this.eyeR.setScale(1);
        }
        tween(config) { const tween = this.tweens.add(config); this.activeEffects.push(tween); return tween; }
        timer(config) { const timer = this.time.addEvent(config); this.activeEffects.push(timer); return timer; }
        blink() { this.timer({ delay: 3100, loop: true, callback: () => this.tweens.add({ targets: [this.eyeL, this.eyeR], scaleY: .08, yoyo: true, duration: 80 }) }); }
        setMouth(index) { this.mouths.forEach((mouth, current) => mouth.setVisible(index === current)); }
        play(action) {
            this.clearMotion(); this.blink();
            if (action === "idle") this.tween({ targets: this.rig, y: 12, duration: 1800, yoyo: true, repeat: -1, ease: "Sine.InOut" });
            if (action === "wave") {
                this.leftArm.setAngle(76);
                this.tween({ targets: this.leftArm, angle: { from: 72, to: 80 }, duration: 700, yoyo: true, repeat: -1, ease: "Sine.InOut" });
                this.tween({ targets: this.leftArm.hand, angle: { from: -5, to: 7 }, duration: 700, yoyo: true, repeat: -1, ease: "Sine.InOut" });
            }
            if (action === "talk") { let frame = 0; this.timer({ delay: 155, loop: true, callback: () => this.setMouth([0, 3, 2, 0][frame++ % 4]) }); this.tween({ targets: this.head, angle: { from: -2, to: 2 }, duration: 650, yoyo: true, repeat: -1 }); }
            if (action === "laugh") { this.setMouth(1); this.eyeL.setScale(1, .2); this.eyeR.setScale(1, .2); this.tween({ targets: this.rig, y: { from: 18, to: 7 }, angle: { from: -1, to: 1 }, duration: 330, yoyo: true, repeat: -1 }); this.laughterTears(); }
            if (action === "think") { this.setMouth(2); this.head.setAngle(-6); this.rightArm.setAngle(-115); this.tween({ targets: this.rightArm, angle: { from: -112, to: -118 }, duration: 1200, yoyo: true, repeat: -1, ease: "Sine.InOut" }); this.questionMarks(); }
            if (action === "fart") {
                this.setMouth(2); this.rig.setPosition(320, 18).setAngle(-8); this.head.setAngle(8);
                this.leftArm.setAngle(18); this.rightArm.setAngle(-22);
                this.tween({ targets: this.rig, angle: { from: -6, to: -10 }, y: { from: 18, to: 23 }, duration: 250, yoyo: true, ease: "Sine.InOut" });
                this.gasCloud();
                this.timer({ delay: 1000, callback: () => this.finishOneShot() });
            }
            if (action === "celebrate") {
                this.setMouth(1); this.leftArm.setAngle(100).setScale(1); this.rightArm.setAngle(-100).setScale(1);
                this.tween({ targets: this.rig, y: { from: 18, to: -12 }, duration: 420, yoyo: true, repeat: -1, ease: "Sine.Out" });
                this.tween({ targets: this.leftArm, angle: { from: 97, to: 103 }, duration: 310, yoyo: true, repeat: -1, ease: "Sine.InOut" });
                this.tween({ targets: this.rightArm, angle: { from: -97, to: -103 }, duration: 310, yoyo: true, repeat: -1, ease: "Sine.InOut" }); this.confetti();
            }
        }
        confetti() {
            [0x24d8e7, 0xffd84d, 0xff5ca8, 0x8cff86, 0xff875c, 0xb497ff].forEach((colour, index) => {
                const key = "presenter-confetti-" + index;
                if (!this.textures.exists(key)) { const graphic = this.make.graphics({ add: false }); graphic.fillStyle(colour).fillRoundedRect(0, 0, 10, 20, 3); graphic.generateTexture(key, 10, 20); graphic.destroy(); }
                const particles = this.add.particles(360, 28, key, { speed: { min: 150, max: 330 }, angle: { min: 205, max: 335 }, gravityY: 330, lifespan: 1650, quantity: 3, frequency: 190, rotate: { min: 0, max: 360 }, scale: { start: .9, end: .45 } });
                this.activeEffects.push({ remove: () => particles.destroy() });
            });
        }
        questionMarks() {
            const colours = ["#ff5ca8", "#ff875c", "#ffd84d", "#78e66b", "#24d8e7", "#9b7cff"];
            colours.forEach((colour, index) => {
                const key = `presenter-question-${index}`;
                if (!this.textures.exists(key)) {
                    const texture = this.textures.createCanvas(key, 36, 46), context = texture.context;
                    context.font = "900 38px Arial"; context.textAlign = "center"; context.textBaseline = "middle"; context.fillStyle = colour; context.fillText("?", 18, 23); texture.refresh();
                }
                const particles = this.add.particles(420, 128, key, { speed: { min: 28, max: 62 }, angle: { min: 235, max: 305 }, lifespan: 1500, frequency: 840, quantity: 1, delay: index * 135, alpha: { start: 1, end: 0 }, scale: { start: .48, end: 1.05 }, rotate: { min: -18, max: 18 } });
                this.activeEffects.push({ remove: () => particles.destroy() });
            });
        }
        laughterTears() {
            const key = "presenter-tear";
            if (!this.textures.exists(key)) { const graphic = this.make.graphics({ add: false }); graphic.fillStyle(0x54d9ff).fillCircle(7, 7, 7).fillTriangle(3, 7, 11, 7, 7, 17); graphic.generateTexture(key, 14, 18); graphic.destroy(); }
            [[334, 126, 145, 190], [386, 126, -10, 35]].forEach(([x, y, min, max]) => {
                const particles = this.add.particles(x, y, key, { speed: { min: 115, max: 205 }, angle: { min, max }, gravityY: 230, lifespan: 820, frequency: 190, quantity: 1, alpha: { start: .95, end: .15 }, scale: { start: .72, end: .25 }, rotate: { min: -35, max: 35 } });
                this.activeEffects.push({ remove: () => particles.destroy() });
            });
        }
        gasCloud() {
            const key = "presenter-gas-cloud";
            if (!this.textures.exists(key)) {
                const texture = this.textures.createCanvas(key, 48, 48), context = texture.context, gradient = context.createRadialGradient(24, 24, 3, 24, 24, 23);
                gradient.addColorStop(0, "rgba(177,255,89,.95)"); gradient.addColorStop(.58, "rgba(79,190,72,.72)"); gradient.addColorStop(1, "rgba(36,112,55,0)"); context.fillStyle = gradient; context.fillRect(0, 0, 48, 48); texture.refresh();
            }
            const particles = this.add.particles(292, 384, key, { speed: { min: 35, max: 90 }, angle: { min: 145, max: 215 }, lifespan: 900, frequency: 90, duration: 450, quantity: 2, alpha: { start: .85, end: 0 }, scale: { start: .35, end: 1.8 }, rotate: { min: 0, max: 360 } });
            this.activeEffects.push({ remove: () => particles.destroy() });
        }
        finishOneShot() {
            this.play("idle");
            this.host.querySelector("[data-animation-label]").textContent = labels.idle[0];
            this.host.querySelector("[data-speech-bubble]").textContent = labels.idle[1];
            this.host.querySelectorAll("[data-presenter-action]").forEach(button => button.classList.toggle("is-selected", button.dataset.presenterAction === "idle"));
        }
        randomise() {
            const skin = pick(skins), presentation = pick(["man", "woman"]), hair = pick(["brown", "black", "blonde", "red"]), shirt = pick(["navy", "blue", "green", "red"]), trousers = pick(["navy", "blue", "green", "tan"]), trouserLength = pick(["long", "cropped", "shorts"]), shoes = pick(["brown", "black", "blue", "red"]);
            const top = presentation === "woman" ? pick([4, 8]) : pick([1, 2, 3, 5, 6, 7]);
            const headKey = "head" + skin.suffix;
            [this.headShape, this.neck, this.leftArm.hand, this.rightArm.hand, this.nose].forEach((part, index) => part.setTexture([headKey, "neck" + skin.suffix, "hand" + skin.suffix, "hand" + skin.suffix, "nose" + skin.suffix][index]));
            const skinPixel = this.textures.getPixel(86, 84, headKey);
            this.neckBacking.clear().fillStyle(Phaser.Display.Color.GetColor(skinPixel.r, skinPixel.g, skinPixel.b)).fillRoundedRect(-37, 154, 74, 92, 30);
            this.hair.setTexture(hair === "brown" && presentation === "man" ? "hair" : `hair-${hair}-${presentation}`).setX(presentation === "man" ? 4 : 0);
            const shirtKey = `top-${shirt}-${top}`, armKey = shirt === "navy" ? "arm" : "arm-" + shirt;
            this.shirt.setTexture(shirtKey); this.leftArm.sleeve.setTexture(armKey); this.rightArm.sleeve.setTexture(armKey);
            const pantsKey = trousers === "navy" ? "pants" : "pants-" + trousers, legKey = trousers === "navy" ? "leg" : "leg-" + trousers;
            const selectedLegKey = trouserLength === "long" ? legKey : trouserLength === "cropped" ? `leg-${trousers}-three-quarter-v2` : `leg-${trousers}-shorts-v2`;
            // Each exported length has a different transparent top contour. Keep
            // the approved shorts anchor, but tuck the longer cuts farther under
            // the hip piece so their outside edges meet the belt cleanly.
            const legX = trouserLength === "shorts" ? 95.5 : 104.5;
            this.leftSkinLeg.setX(-legX); this.rightSkinLeg.setX(legX);
            this.leftLeg.setX(-legX); this.rightLeg.setX(legX);
            // Exposed ankles need the shoes directly beneath them; the longer
            // trouser cuts already meet the existing shoe positions correctly.
            const shoeX = trouserLength === "shorts" ? 78 : trouserLength === "cropped" ? 82 : 66;
            const shoeY = trouserLength === "shorts" ? 514 : trouserLength === "cropped" ? 505 : 513;
            this.leftShoe.setPosition(-shoeX, shoeY); this.rightShoe.setPosition(shoeX, shoeY);
            this.pants.setTexture(pantsKey); this.leftLeg.setTexture(selectedLegKey); this.rightLeg.setTexture(selectedLegKey);
            this.leftSkinLeg.setTexture("skin-leg" + skin.suffix).setVisible(trouserLength !== "long");
            this.rightSkinLeg.setTexture("skin-leg" + skin.suffix).setVisible(trouserLength !== "long");
            const shoeKey = shoes === "brown" ? "shoe-smart" : "shoe-smart-" + shoes; this.leftShoe.setTexture(shoeKey); this.rightShoe.setTexture(shoeKey);
            const topNames = { 1: "plain top", 2: "hoodie top", 3: "pocket top", 4: "fitted top", 5: "collared top", 6: "zip top", 7: "zip-pocket top", 8: "fitted zip top" };
            const lengthNames = { long: "full-length trousers", cropped: "three-quarter trousers", shorts: "shorts" };
            this.host.querySelector("[data-presenter-profile]").textContent = `${presentation === "woman" ? "Woman" : "Man"} · ${topNames[top]} · ${lengthNames[trouserLength]} · ${skin.name} skin · ${hair} hair`;
        }
    }

    const initialise = root => {
        if (root.dataset.presenterReady === "true") return; root.dataset.presenterReady = "true";
        const label = root.querySelector("[data-animation-label]"), bubble = root.querySelector("[data-speech-bubble]"), buttons = [...root.querySelectorAll("[data-presenter-action]")];
        new Phaser.Game({ type: Phaser.AUTO, parent: root.querySelector("[data-presenter-canvas]"), transparent: true, width: 720, height: 560, scene: new PresenterScene(root), scale: { mode: Phaser.Scale.FIT, autoCenter: Phaser.Scale.CENTER_BOTH } });
        root.querySelector("[data-randomise-presenter]").addEventListener("click", () => root.presenterScene?.randomise());
        buttons.forEach(button => button.addEventListener("click", () => {
            const action = button.dataset.presenterAction; if (!root.presenterScene) return; root.presenterScene.play(action);
            label.textContent = labels[action][0]; bubble.textContent = labels[action][1]; buttons.forEach(item => item.classList.toggle("is-selected", item === button));
            bubble.animate([{ opacity: .25, transform: "translateY(8px) rotate(2deg)" }, { opacity: 1, transform: "translateY(0) rotate(2deg)" }], { duration: 260, easing: "ease-out" });
        }));
    };
    const scan = () => document.querySelectorAll("[data-presenter-lab]").forEach(initialise); scan(); new MutationObserver(scan).observe(document.documentElement, { childList: true, subtree: true });
})();
