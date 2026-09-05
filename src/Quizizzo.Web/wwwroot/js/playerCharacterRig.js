window.quizizzoCharacterRig = (() => {
    const assetRoot = "/assets/kenney-presenter/spritesheets/";
    const sheets = ["face", "hair", "pants", "shirts", "shoes", "skin"];

    const boundedNumber = (value, prefix, minimum, maximum, fallback = minimum) => {
        const normalized = typeof value === "string" && prefix && value.startsWith(prefix)
            ? value.slice(prefix.length)
            : value;
        const parsed = Number(normalized);
        return Number.isFinite(parsed) ? Math.min(Math.max(parsed, minimum), maximum) : fallback;
    };

    const paletteIndex = primaryColour => {
        if (typeof primaryColour !== "string" || !/^#[0-9a-f]{6}$/i.test(primaryColour)) return 0;
        return Number.parseInt(primaryColour.slice(1), 16) % 4;
    };

    function loadAtlases(scene, prefix) {
        sheets.forEach(sheet => scene.load.atlasXML(
            `${prefix}${sheet}`,
            `${assetRoot}sheet_${sheet}.png`,
            `${assetRoot}sheet_${sheet}.xml`));
    }

    function resolve(character = {}) {
        const legacyBodySkin = { Bean: 1, Blob: 3, Round: 5, Square: 7 };
        const requestedSkin = boundedNumber(character.skinTone, "Tint", 1, 8, 1);
        const skin = [1, 3, 5, 7].includes(requestedSkin)
            ? requestedSkin
            : legacyBodySkin[character.bodyType] || requestedSkin;
        const presentation = character.presentation ||
            (["Blob", "Square"].includes(character.bodyType) ? "Woman" : "Man");
        const bodyWidth = { Thin: .84, Normal: 1, Regular: 1, Thick: 1.16 }[character.bodySize] || 1;
        const hairPrefix = { Brown: "brown1", Black: "black", Blonde: "blonde", Red: "red" }[character.hairColour];
        const legacyHair = {
            Bright: `blonde${presentation}1.png`,
            Sleepy: `brown1${presentation}${presentation === "Man" ? 5 : 3}.png`,
            Starry: `red${presentation}${presentation === "Man" ? 1 : 4}.png`,
            Googly: `black${presentation}${presentation === "Man" ? 2 : 1}.png`
        }[character.eyes];
        const maximumHairStyle = presentation === "Woman" ? 6 : 8;
        const hairStyle = boundedNumber(character.hairStyle, "Style", 1, maximumHairStyle, 1);
        const hair = hairPrefix ? `${hairPrefix}${presentation}${hairStyle}.png` : legacyHair;
        const eyeColour = ["Black", "Blue", "Brown", "Green", "Pine"].includes(character.eyeColour)
            ? character.eyeColour : character.eyes === "Sleepy" ? "Brown" : "Blue";
        const eyeSize = character.eyeSize === "Small" ? "small" : "large";
        const browPrefix = character.eyes === "Googly" ? "black"
            : character.eyes === "Starry" ? "red"
                : character.eyes === "Bright" ? "blonde" : "brown1";
        const browShape = boundedNumber(character.browShape, "Brow", 1, 3, 1);
        const noseShape = boundedNumber(character.noseShape, "Nose", 1, 3, 1);
        const faceWidth = { Oval: .92, Round: 1, Wide: 1.1 }[character.faceShape] || 1;
        const mouth = {
            Smile: "mouth_happy.png", Grin: "mouth_teethUpper.png",
            TeethLower: "mouth_teethLower.png", Surprised: "mouth_oh.png",
            Tongue: "mouth_glad.png", Sad: "mouth_sad.png",
            Straight: "mouth_straight.png"
        }[character.mouth] || "mouth_happy.png";
        const index = paletteIndex(character.primaryColour);
        const shirt = { Navy: "navy", Blue: "blue", Green: "green", Red: "red" }[character.shirtColour]
            || ["navy", "blue", "green", "red"][index];
        const pants = { Navy: "pantsNavy", Blue: "pantsBlue1", Green: "pantsGreen", Tan: "pantsTan" }[character.trouserColour]
            || ["pantsNavy", "pantsBlue1", "pantsGreen", "pantsTan"][index];
        const shoeStyle = boundedNumber(character.shoeStyle, "Style", 1, 5, 1);
        const shoePrefix = { Brown: "brown", Black: "black", Blue: "blue", Red: "red" }[character.shoeColour];
        const shoe = shoePrefix ? `${shoePrefix}Shoe${shoeStyle}.png`
            : ["brownShoe1.png", "blackShoe1.png", "blueShoe1.png", "redShoe1.png"][index];
        const trouserLength = { FullLength: "long", Cropped: "short", Shorts: "shorter" }[character.trouserLength] || "long";
        const allowedShirtStyles = presentation === "Woman" ? [4, 8] : [1, 2, 3, 5, 6, 7];
        const requestedShirtStyle = boundedNumber(character.shirtStyle, "Style", 1, 8, allowedShirtStyles[0]);
        const shirtStyle = allowedShirtStyles.includes(requestedShirtStyle)
            ? requestedShirtStyle : allowedShirtStyles[0];
        const trouserStyle = boundedNumber(character.trouserStyle, "Style", 1, 4, 1);
        return {
            skin, presentation, bodyWidth, hair,
            eye: `eye${eyeColour}_${eyeSize}.png`,
            brow: `${hairPrefix || browPrefix}Brow${browShape}.png`,
            mouth, faceWidth, noseShape, shirt, shirtStyle, pants,
            trouserStyle, trouserLength, shoe
        };
    }

    function create(scene, {
        container,
        atlasPrefix,
        includeGroundShadow = false,
        armsInFront = false,
        headOffsetY = 0,
        handInset = 0
    } = {}) {
        const target = container || scene.add.container(0, 0);
        const animationTweens = [];
        const effects = [];
        let timer = null;
        let variants = null;
        let parts = null;
        let animationOrigin = null;

        const rememberTween = tween => {
            animationTweens.push(tween);
            return tween;
        };
        const add = (x, y, atlas, frame, originX = .5, originY = .5) => {
            const image = scene.add.image(x, y, `${atlasPrefix}${atlas}`, frame).setOrigin(originX, originY);
            target.add(image);
            return image;
        };
        const clearAnimation = (restore = true) => {
            animationTweens.splice(0).forEach(tween => tween.stop());
            timer?.remove(false);
            timer = null;
            effects.splice(0).forEach(effect => effect.destroy());
            if (restore && animationOrigin) {
                target.setPosition(animationOrigin.x, animationOrigin.y)
                    .setAngle(animationOrigin.angle)
                    .setScale(animationOrigin.scaleX, animationOrigin.scaleY);
                parts?.eyeLeft?.setScale(1);
                parts?.eyeRight?.setScale(1);
                parts?.mouth?.setTexture(`${atlasPrefix}face`, variants.mouth);
                parts?.armLeft?.setAngle(0);
                parts?.armRight?.setAngle(0);
                parts?.armLeft?.setY(218);
                parts?.armRight?.setY(218);
                parts?.handLeft?.setAngle(0);
                parts?.handRight?.setAngle(0);
                parts?.handLeft?.setPosition(-108 + handInset, 83 - handInset * .45);
                parts?.handRight?.setPosition(108 - handInset, 83 - handInset * .45);
                parts?.sleeveLeft?.setScale(1);
                parts?.sleeveRight?.setScale(1);
                parts?.shoeLeft?.setAngle(0);
                parts?.shoeRight?.setAngle(0);
            }
            animationOrigin = null;
        };

        const render = (character, mode = "full") => {
            clearAnimation(true);
            target.removeAll(true);
            variants = resolve(character);
            parts = {};
            if (mode === "full") {
                if (includeGroundShadow) {
                    const shadow = scene.add.ellipse(0, 523, 250 * variants.bodyWidth, 30, 0x02091f, .42);
                    target.add(shadow);
                    parts.shadow = shadow;
                }
                const bodyParts = [];
                const body = (...args) => {
                    const part = add(...args);
                    bodyParts.push(part);
                    return part;
                };
                const arm = (x, left) => {
                    const group = scene.add.container(x, 218);
                    const sleeve = scene.add.image(0, 0, `${atlasPrefix}shirts`,
                        `${variants.shirt}Arm_long.png`)
                        .setOrigin(left ? .69 : .31, .18)
                        .setFlipX(left);
                    const hand = scene.add.image(
                        left ? -108 + handInset : 108 - handInset,
                        83 - handInset * .45,
                        `${atlasPrefix}skin`, `tint${variants.skin}_hand.png`)
                        .setOrigin(.5, .12);
                    group.add([sleeve, hand]);
                    target.add(group);
                    bodyParts.push(group);
                    return { group, sleeve, hand };
                };
                body(0, 168, "skin", `tint${variants.skin}_neck.png`, .5, 0).setScale(.42, 1);
                const leftArm = arm(-58, true);
                const rightArm = arm(58, false);
                parts.armLeft = leftArm.group;
                parts.armRight = rightArm.group;
                parts.sleeveLeft = leftArm.sleeve;
                parts.sleeveRight = rightArm.sleeve;
                parts.handLeft = leftArm.hand;
                parts.handRight = rightArm.hand;
                body(-95.5, 341, "skin", `tint${variants.skin}_leg.png`, 0, 0).setFlipX(true);
                body(95.5, 341, "skin", `tint${variants.skin}_leg.png`, 1, 0);
                body(-95.5, 341, "pants", `${variants.pants}_${variants.trouserLength}.png`, 0, 0).setFlipX(true);
                body(95.5, 341, "pants", `${variants.pants}_${variants.trouserLength}.png`, 1, 0);
                parts.shoeLeft = body(-66, 505, "shoes", variants.shoe).setFlipX(true).setScale(.86);
                parts.shoeRight = body(66, 505, "shoes", variants.shoe).setScale(.86);
                body(0, 200, "shirts", `${variants.shirt}Shirt${variants.shirtStyle}.png`, .5, 0);
                body(0, 341, "pants", `${variants.pants}${variants.trouserStyle}.png`, .5, 0);
                bodyParts.forEach(part => {
                    part.x *= variants.bodyWidth;
                    part.scaleX *= variants.bodyWidth;
                });
            }
            // Full-body atlas heads need a little overlap with the neck. Keeping a
            // minimum attachment offset prevents fast stage motion exposing a gap.
            const attachedHeadOffsetY = mode === "full" ? Math.max(18, headOffsetY) : headOffsetY;
            parts.head = add(0, attachedHeadOffsetY, "skin", `tint${variants.skin}_head.png`, .5, 0)
                .setScale(variants.faceWidth, 1);
            parts.hair = add(0, -25 + attachedHeadOffsetY, "hair", variants.hair, .5, 0);
            parts.eyeLeft = add(-27 * variants.faceWidth, 75 + attachedHeadOffsetY, "face", variants.eye);
            parts.eyeRight = add(27 * variants.faceWidth, 75 + attachedHeadOffsetY, "face", variants.eye);
            parts.browLeft = add(-28 * variants.faceWidth, 55 + attachedHeadOffsetY, "face", variants.brow);
            parts.browRight = add(28 * variants.faceWidth, 55 + attachedHeadOffsetY, "face", variants.brow).setFlipX(true);
            parts.nose = add(0, 98 + attachedHeadOffsetY, "face", `tint${variants.skin}Nose${variants.noseShape}.png`);
            parts.mouth = add(0, 132 + attachedHeadOffsetY, "face", variants.mouth);
            if (armsInFront && mode === "full") {
                target.bringToTop(parts.armLeft);
                target.bringToTop(parts.armRight);
                [parts.head, parts.hair, parts.eyeLeft, parts.eyeRight, parts.browLeft,
                    parts.browRight, parts.nose, parts.mouth].forEach(part => target.bringToTop(part));
            }
            return variants;
        };

        const play = (action, { resumeIdle = false, onComplete = null, beatMs = 480 } = {}) => {
            clearAnimation(true);
            if (!parts || !variants) return;
            animationOrigin = {
                x: target.x, y: target.y, angle: target.angle,
                scaleX: target.scaleX, scaleY: target.scaleY
            };
            const origin = animationOrigin;
            const finish = () => {
                clearAnimation(true);
                onComplete?.();
                if (resumeIdle) play("idle");
            };
            if (action === "idle") {
                // Keep the hands relaxed at waist height while the whole rig
                // rises and falls almost imperceptibly like gentle breathing.
                parts.armLeft?.setAngle(-18);
                parts.armRight?.setAngle(18);
                if (parts.armLeft && parts.armRight) {
                    rememberTween(scene.tweens.add({
                        targets: parts.armLeft, angle: { from: -19.5, to: -16.5 },
                        duration: 2200, yoyo: true, repeat: -1, ease: "Sine.easeInOut"
                    }));
                    rememberTween(scene.tweens.add({
                        targets: parts.armRight, angle: { from: 16.5, to: 19.5 },
                        duration: 2200, yoyo: true, repeat: -1, ease: "Sine.easeInOut"
                    }));
                }
                rememberTween(scene.tweens.add({
                    targets: target, y: origin.y - 2.5, duration: 2200,
                    yoyo: true, repeat: -1, ease: "Sine.easeInOut"
                }));
                return;
            }
            if (action === "talk") {
                let mouthFrame = 0;
                const mouthFrames = [variants.mouth, "mouth_teethUpper.png", "mouth_oh.png", variants.mouth];
                timer = scene.time.addEvent({
                    delay: 155,
                    loop: true,
                    callback: () => parts.mouth.setTexture(
                        `${atlasPrefix}face`, mouthFrames[mouthFrame++ % mouthFrames.length])
                });
                // Rotate from the shoulders so both hands rest lower and closer
                // to the body instead of holding the atlas's wide default pose.
                parts.armLeft?.setAngle(-18);
                parts.armRight?.setAngle(18);
                if (parts.armLeft && parts.armRight) {
                    rememberTween(scene.tweens.add({
                        targets: parts.armLeft, angle: { from: -22, to: -14 },
                        duration: 720, yoyo: true, repeat: -1, ease: "Sine.easeInOut"
                    }));
                    rememberTween(scene.tweens.add({
                        targets: parts.armRight, angle: { from: 14, to: 22 },
                        duration: 720, yoyo: true, repeat: -1, ease: "Sine.easeInOut"
                    }));
                }
                rememberTween(scene.tweens.add({
                    targets: target, y: origin.y - 4, angle: { from: -.7, to: .7 },
                    duration: 820, yoyo: true, repeat: -1, ease: "Sine.easeInOut"
                }));
                return;
            }
            if (action === "laugh") {
                parts.mouth.setTexture(`${atlasPrefix}face`, "mouth_glad.png");
                parts.eyeLeft.setScale(1, .22);
                parts.eyeRight.setScale(1, .22);
                rememberTween(scene.tweens.add({
                    targets: target, y: origin.y - 9, angle: { from: -1.8, to: 1.8 },
                    duration: 280, yoyo: true, repeat: -1, ease: "Sine.easeInOut"
                }));
                return;
            }
            if (action === "cry") {
                parts.mouth.setTexture(`${atlasPrefix}face`, "mouth_sad.png");
                parts.eyeLeft.setScale(1, .35);
                parts.eyeRight.setScale(1, .35);
                [-27, 27].forEach((x, index) => {
                    const tear = scene.add.circle(x * variants.faceWidth, 86, 7, 0x38bdf8, .95);
                    target.add(tear);
                    effects.push(tear);
                    rememberTween(scene.tweens.add({
                        targets: tear, y: 155, alpha: 0, duration: 760,
                        delay: index * 230, repeat: -1, repeatDelay: 220
                    }));
                });
                rememberTween(scene.tweens.add({
                    targets: target, angle: { from: -1.2, to: 1.2 },
                    duration: 520, yoyo: true, repeat: -1, ease: "Sine.easeInOut"
                }));
                return;
            }
            if (action === "celebrate") {
                parts.mouth.setTexture(`${atlasPrefix}face`, "mouth_glad.png");
                parts.eyeLeft.setScale(1, .65);
                parts.eyeRight.setScale(1, .65);
                parts.armLeft?.setAngle(78);
                parts.armRight?.setAngle(-78);
                if (parts.armLeft && parts.armRight) {
                    rememberTween(scene.tweens.add({
                        targets: parts.armLeft, angle: { from: 73, to: 83 },
                        duration: 340, yoyo: true, repeat: -1, ease: "Sine.easeInOut"
                    }));
                    rememberTween(scene.tweens.add({
                        targets: parts.armRight, angle: { from: -73, to: -83 },
                        duration: 340, yoyo: true, repeat: -1, ease: "Sine.easeInOut"
                    }));
                }
                rememberTween(scene.tweens.add({
                    targets: target,
                    y: { from: origin.y, to: origin.y - 22 },
                    angle: { from: -1.2, to: 1.2 },
                    duration: 430, yoyo: true, repeat: -1, ease: "Sine.easeOut"
                }));
                return;
            }
            if (["bowLegged", "armFlap", "fistPump", "discoPoint", "rubberRobot"].includes(action)) {
                const beat = Math.max(220, Math.min(760, Number(beatMs) || 480));
                const motion = beat * 1.25;
                parts.mouth.setTexture(`${atlasPrefix}face`, "mouth_glad.png");
                if (parts.handLeft && parts.handRight) {
                    rememberTween(scene.tweens.add({
                        targets: parts.handLeft, angle: { from: -18, to: 24 },
                        duration: motion * .72, yoyo: true, repeat: -1, ease: "Sine.easeInOut"
                    }));
                    rememberTween(scene.tweens.add({
                        targets: parts.handRight, angle: { from: 20, to: -22 },
                        duration: motion * .81, yoyo: true, repeat: -1, ease: "Sine.easeInOut"
                    }));
                    // The atlas arm is one rigid piece, so counter-moving the hands
                    // and sleeves creates a soft inflatable-tube wave without joints
                    // folding behind the torso.
                    rememberTween(scene.tweens.add({
                        targets: parts.handLeft, x: { from: -94, to: -113 }, y: { from: 76, to: 91 },
                        duration: motion * .57, yoyo: true, repeat: -1, ease: "Sine.easeInOut"
                    }));
                    rememberTween(scene.tweens.add({
                        targets: parts.handRight, x: { from: 113, to: 94 }, y: { from: 91, to: 76 },
                        duration: motion * .63, yoyo: true, repeat: -1, ease: "Sine.easeInOut"
                    }));
                }
                if (parts.sleeveLeft && parts.sleeveRight) {
                    rememberTween(scene.tweens.add({
                        targets: parts.sleeveLeft, scaleY: { from: .94, to: 1.06 },
                        duration: motion * .57, yoyo: true, repeat: -1, ease: "Sine.easeInOut"
                    }));
                    rememberTween(scene.tweens.add({
                        targets: parts.sleeveRight, scaleY: { from: 1.06, to: .94 },
                        duration: motion * .63, yoyo: true, repeat: -1, ease: "Sine.easeInOut"
                    }));
                }
                if (parts.shoeLeft && parts.shoeRight) {
                    rememberTween(scene.tweens.add({
                        targets: parts.shoeLeft, angle: { from: -5, to: 7 },
                        duration: motion * .72, yoyo: true, repeat: -1, ease: "Sine.easeInOut"
                    }));
                    rememberTween(scene.tweens.add({
                        targets: parts.shoeRight, angle: { from: 7, to: -5 },
                        duration: motion * .72, yoyo: true, repeat: -1, ease: "Sine.easeInOut"
                    }));
                }
                if (parts.armLeft && parts.armRight) {
                    rememberTween(scene.tweens.add({
                        targets: parts.armLeft, y: { from: 211, to: 224 },
                        duration: motion * .78, yoyo: true, repeat: -1, ease: "Sine.easeInOut"
                    }));
                    rememberTween(scene.tweens.add({
                        targets: parts.armRight, y: { from: 224, to: 211 },
                        duration: motion * .86, yoyo: true, repeat: -1, ease: "Sine.easeInOut"
                    }));
                }
                const sway = angle => {
                    const pose = { angle: -angle };
                    const footPivotY = 523;
                    return rememberTween(scene.tweens.add({
                        targets: pose,
                        angle,
                        duration: motion,
                        yoyo: true,
                        repeat: -1,
                        ease: "Sine.easeInOut",
                        onUpdate: () => {
                            const radians = Phaser.Math.DegToRad(pose.angle);
                            target.setAngle(pose.angle);
                            target.setPosition(
                                origin.x + Math.sin(radians) * footPivotY * origin.scaleY,
                                origin.y + (1 - Math.cos(radians)) * footPivotY * origin.scaleY);
                        }
                    }));
                };
                if (action === "bowLegged") {
                    parts.armLeft?.setAngle(-38);
                    parts.armRight?.setAngle(38);
                    if (parts.armLeft && parts.armRight) {
                        rememberTween(scene.tweens.add({ targets: parts.armLeft,
                            angle: { from: -32, to: -62 }, duration: motion,
                            yoyo: true, repeat: -1, ease: "Sine.easeInOut" }));
                        rememberTween(scene.tweens.add({ targets: parts.armRight,
                            angle: { from: 32, to: 62 }, duration: motion,
                            yoyo: true, repeat: -1, ease: "Sine.easeInOut" }));
                    }
                    sway(5);
                } else if (action === "armFlap") {
                    rememberTween(scene.tweens.add({ targets: parts.armLeft, angle: { from: -34, to: -82 },
                        duration: motion, yoyo: true, repeat: -1, ease: "Sine.easeInOut" }));
                    rememberTween(scene.tweens.add({ targets: parts.armRight, angle: { from: 34, to: 82 },
                        duration: motion, yoyo: true, repeat: -1, ease: "Sine.easeInOut" }));
                    sway(3);
                } else if (action === "fistPump") {
                    rememberTween(scene.tweens.add({ targets: parts.armLeft, angle: { from: -18, to: -48 },
                        duration: motion, yoyo: true, repeat: -1, ease: "Sine.easeInOut" }));
                    rememberTween(scene.tweens.add({ targets: parts.armRight, angle: { from: 48, to: 84 },
                        duration: motion * .72, yoyo: true, repeat: -1, ease: "Sine.easeInOut" }));
                    sway(4);
                } else if (action === "discoPoint") {
                    rememberTween(scene.tweens.add({ targets: parts.armLeft, angle: { from: -86, to: -58 },
                        duration: motion * 1.15, yoyo: true, repeat: -1, ease: "Sine.easeInOut" }));
                    rememberTween(scene.tweens.add({ targets: parts.armRight, angle: { from: 58, to: 18 },
                        duration: motion * 1.15, yoyo: true, repeat: -1, ease: "Sine.easeInOut" }));
                    sway(6);
                } else {
                    rememberTween(scene.tweens.add({ targets: parts.armLeft, angle: { from: -72, to: -24 },
                        duration: motion, yoyo: true, repeat: -1, ease: "Sine.easeInOut" }));
                    rememberTween(scene.tweens.add({ targets: parts.armRight, angle: { from: 24, to: 72 },
                        duration: motion, yoyo: true, repeat: -1, ease: "Sine.easeInOut" }));
                    sway(4);
                }
                return;
            }
            if (action === "dazed") {
                parts.mouth.setTexture(`${atlasPrefix}face`, "mouth_oh.png");
                parts.eyeLeft.setScale(1, .35);
                parts.eyeRight.setScale(1, .35);
                parts.armLeft?.setAngle(-12);
                parts.armRight?.setAngle(12);
                const starGlyphs = ["★", "✦", "★"];
                starGlyphs.forEach((glyph, index) => {
                    const star = scene.add.text(-62 + index * 62, -42 - (index % 2) * 18, glyph, {
                        color: index === 1 ? "#67e8f9" : "#fde047",
                        fontFamily: "Arial", fontSize: "44px", fontStyle: "bold",
                        stroke: "#3b0764", strokeThickness: 5
                    }).setOrigin(.5);
                    target.add(star);
                    effects.push(star);
                    rememberTween(scene.tweens.add({ targets: star, angle: 360, x: star.x + 22,
                        duration: 520 + index * 90, repeat: -1, ease: "Linear" }));
                });
                rememberTween(scene.tweens.add({ targets: target, angle: { from: -5, to: 5 },
                    duration: 120, yoyo: true, repeat: 7, ease: "Sine.easeInOut" }));
                timer = scene.time.delayedCall(1000, finish);
                return;
            }
            if (action === "fart") {
                rememberTween(scene.tweens.add({
                    targets: target, x: origin.x - 12, angle: 7,
                    duration: 180, yoyo: true, hold: 420, ease: "Quad.easeOut"
                }));
                [0, 1, 2].forEach(index => {
                    const cloud = scene.add.circle(92 * variants.bodyWidth, 405 - index * 8,
                        19 + index * 5, index === 1 ? 0x65a30d : 0x84cc16, .68);
                    target.add(cloud);
                    effects.push(cloud);
                    rememberTween(scene.tweens.add({
                        targets: cloud, x: 195 * variants.bodyWidth + index * 18,
                        y: cloud.y - 35 - index * 9, scale: 1.7, alpha: 0,
                        duration: 850, delay: index * 90, ease: "Cubic.easeOut"
                    }));
                });
                timer = scene.time.delayedCall(1000, finish);
                return;
            }
            finish();
        };

        return {
            container: target,
            render,
            play,
            stop: () => clearAnimation(true),
            destroy: () => {
                clearAnimation(false);
                target.removeAll(true);
            },
            get variants() { return variants; }
        };
    }

    return { sheets, loadAtlases, resolve, create };
})();
