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

    function create(scene, { container, atlasPrefix, includeGroundShadow = false } = {}) {
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
                target.setPosition(animationOrigin.x, animationOrigin.y).setAngle(animationOrigin.angle);
                parts?.eyeLeft?.setScale(1);
                parts?.eyeRight?.setScale(1);
                parts?.mouth?.setTexture(`${atlasPrefix}face`, variants.mouth);
                parts?.armLeft?.setAngle(0);
                parts?.armRight?.setAngle(0);
                parts?.handLeft?.setAngle(0);
                parts?.handRight?.setAngle(0);
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
                    const hand = scene.add.image(left ? -108 : 108, 83,
                        `${atlasPrefix}skin`, `tint${variants.skin}_hand.png`)
                        .setOrigin(.5, .12);
                    group.add([sleeve, hand]);
                    target.add(group);
                    bodyParts.push(group);
                    return { group, hand };
                };
                body(0, 168, "skin", `tint${variants.skin}_neck.png`, .5, 0).setScale(.42, 1);
                const leftArm = arm(-58, true);
                const rightArm = arm(58, false);
                parts.armLeft = leftArm.group;
                parts.armRight = rightArm.group;
                parts.handLeft = leftArm.hand;
                parts.handRight = rightArm.hand;
                body(-95.5, 341, "skin", `tint${variants.skin}_leg.png`, 0, 0).setFlipX(true);
                body(95.5, 341, "skin", `tint${variants.skin}_leg.png`, 1, 0);
                body(-95.5, 341, "pants", `${variants.pants}_${variants.trouserLength}.png`, 0, 0).setFlipX(true);
                body(95.5, 341, "pants", `${variants.pants}_${variants.trouserLength}.png`, 1, 0);
                body(-66, 505, "shoes", variants.shoe).setFlipX(true).setScale(.86);
                body(66, 505, "shoes", variants.shoe).setScale(.86);
                body(0, 200, "shirts", `${variants.shirt}Shirt${variants.shirtStyle}.png`, .5, 0);
                body(0, 341, "pants", `${variants.pants}${variants.trouserStyle}.png`, .5, 0);
                bodyParts.forEach(part => {
                    part.x *= variants.bodyWidth;
                    part.scaleX *= variants.bodyWidth;
                });
            }
            parts.head = add(0, 0, "skin", `tint${variants.skin}_head.png`, .5, 0)
                .setScale(variants.faceWidth, 1);
            parts.hair = add(0, -25, "hair", variants.hair, .5, 0);
            parts.eyeLeft = add(-27 * variants.faceWidth, 75, "face", variants.eye);
            parts.eyeRight = add(27 * variants.faceWidth, 75, "face", variants.eye);
            parts.browLeft = add(-28 * variants.faceWidth, 55, "face", variants.brow);
            parts.browRight = add(28 * variants.faceWidth, 55, "face", variants.brow).setFlipX(true);
            parts.nose = add(0, 98, "face", `tint${variants.skin}Nose${variants.noseShape}.png`);
            parts.mouth = add(0, 132, "face", variants.mouth);
            return variants;
        };

        const play = (action, { resumeIdle = false, onComplete = null } = {}) => {
            clearAnimation(true);
            if (!parts || !variants) return;
            animationOrigin = { x: target.x, y: target.y, angle: target.angle };
            const origin = animationOrigin;
            const finish = () => {
                clearAnimation(true);
                onComplete?.();
                if (resumeIdle) play("idle");
            };
            if (action === "idle") {
                rememberTween(scene.tweens.add({
                    targets: target, y: origin.y - 5, duration: 1650,
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
