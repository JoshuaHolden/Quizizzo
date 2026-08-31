(() => {
    const assetRoot = "/assets/kenney-presenter/spritesheets/";
    const sheets = ["face", "hair", "pants", "shirts", "shoes", "skin"];
    const games = new Map();

    function values(form) {
        return Object.fromEntries(new FormData(form).entries());
    }

    function syncHairStyles(form) {
        const select = form.querySelector("[data-hair-style]");
        if (!select) return;
        const presentation = form.elements.presentation?.value || "Man";
        const maximum = presentation === "Woman" ? 6 : 8;
        const previous = Number(select.value.replace("Style", "")) || 1;
        select.replaceChildren(...Array.from({ length: maximum }, (_, index) => {
            const option = document.createElement("option");
            option.value = `Style${index + 1}`;
            option.textContent = `Style ${index + 1}`;
            return option;
        }));
        select.value = `Style${Math.min(previous, maximum)}`;
    }

    function syncShirtStyles(form) {
        const select = form.querySelector("[data-shirt-style]");
        if (!select) return;
        const presentation = form.elements.presentation?.value || "Man";
        const styles = presentation === "Woman" ? [4, 8] : [1, 2, 3, 5, 6, 7];
        const previous = Number(select.value.replace("Style", ""));
        select.replaceChildren(...styles.map(style => {
            const option = document.createElement("option");
            option.value = `Style${style}`;
            option.textContent = `Style ${style}`;
            return option;
        }));
        select.value = `Style${styles.includes(previous) ? previous : styles[0]}`;
    }

    function setupTabs(form) {
        const tabs = [...form.querySelectorAll("[data-avatar-tab]")];
        const panels = [...form.querySelectorAll("[data-avatar-panel]")];
        if (!tabs.length || !panels.length) return;

        const activate = (tab, focus = false) => {
            const section = tab.dataset.avatarTab;
            tabs.forEach(item => {
                const selected = item === tab;
                item.setAttribute("aria-selected", selected ? "true" : "false");
                item.tabIndex = selected ? 0 : -1;
            });
            panels.forEach(panel => { panel.hidden = panel.dataset.avatarPanel !== section; });
            if (focus) tab.focus();
        };

        tabs.forEach((tab, index) => {
            tab.addEventListener("click", () => activate(tab));
            tab.addEventListener("keydown", event => {
                const direction = event.key === "ArrowRight" ? 1 : event.key === "ArrowLeft" ? -1 : 0;
                if (!direction) return;
                event.preventDefault();
                activate(tabs[(index + direction + tabs.length) % tabs.length], true);
            });
        });
    }

    class AvatarDesignerScene extends Phaser.Scene {
        constructor(form, host) {
            super(`avatar-designer-${crypto.randomUUID()}`);
            this.form = form;
            this.host = host;
        }

        preload() {
            sheets.forEach(sheet => this.load.atlasXML(
                `designer-${sheet}`,
                `${assetRoot}sheet_${sheet}.png`,
                `${assetRoot}sheet_${sheet}.xml`));
        }

        create() {
            this.rig = this.add.container(320, 10).setScale(.58);
            this.renderChoice();
        }

        addPart(x, y, atlas, frame, originX = .5, originY = .5) {
            const image = this.add.image(x, y, `designer-${atlas}`, frame).setOrigin(originX, originY);
            this.rig.add(image);
            return image;
        }

        renderChoice() {
            if (!this.rig) return;
            this.rig.removeAll(true);
            const choice = values(this.form);
            const skin = Number(choice.skinTone?.replace("Tint", "")) || 1;
            const presentation = choice.presentation || "Man";
            const bodyWidth = { Thin: .84, Normal: 1, Thick: 1.16 }[choice.bodySize] || 1;
            const hairPrefix = { Brown: "brown1", Black: "black", Blonde: "blonde", Red: "red" }[choice.hairColour] || "brown1";
            const maximumHairStyle = presentation === "Woman" ? 6 : 8;
            const hairStyle = Math.min(Number(choice.hairStyle?.replace("Style", "")) || 1, maximumHairStyle);
            const faceWidth = { Oval: .92, Round: 1, Wide: 1.1 }[choice.faceShape] || 1;
            const eyeColour = ["Black", "Blue", "Brown", "Green", "Pine"].includes(choice.eyeColour) ? choice.eyeColour : "Blue";
            const eyeSize = choice.eyeSize === "Small" ? "small" : "large";
            const browShape = Math.min(Number(choice.browShape?.replace("Brow", "")) || 1, 3);
            const noseShape = Math.min(Number(choice.noseShape?.replace("Nose", "")) || 1, 3);
            const mouth = {
                Smile: "mouth_happy.png", Grin: "mouth_teethUpper.png",
                TeethLower: "mouth_teethLower.png", Surprised: "mouth_oh.png",
                Tongue: "mouth_glad.png", Sad: "mouth_sad.png",
                Straight: "mouth_straight.png"
            }[choice.mouth] || "mouth_happy.png";
            const shirt = { Navy: "navy", Blue: "blue", Green: "green", Red: "red" }[choice.shirtColour] || "navy";
            const allowedShirtStyles = presentation === "Woman" ? [4, 8] : [1, 2, 3, 5, 6, 7];
            const requestedShirtStyle = Number(choice.shirtStyle?.replace("Style", ""));
            const shirtStyle = allowedShirtStyles.includes(requestedShirtStyle) ? requestedShirtStyle : allowedShirtStyles[0];
            const pants = { Navy: "pantsNavy", Blue: "pantsBlue1", Green: "pantsGreen", Tan: "pantsTan" }[choice.trouserColour] || "pantsNavy";
            const trouserStyle = Math.min(Number(choice.trouserStyle?.replace("Style", "")) || 1, 4);
            const length = { FullLength: "long", Cropped: "short", Shorts: "shorter" }[choice.trouserLength] || "long";
            const shoeStyle = Math.min(Number(choice.shoeStyle?.replace("Style", "")) || 1, 5);
            const shoePrefix = { Brown: "brown", Black: "black", Blue: "blue", Red: "red" }[choice.shoeColour] || "brown";
            const shoe = `${shoePrefix}Shoe${shoeStyle}.png`;
            const hair = `${hairPrefix}${presentation}${hairStyle}.png`;
            const shirtFrame = `${shirt}Shirt${shirtStyle}.png`;

            this.rig.add(this.add.ellipse(0, 523, 250 * bodyWidth, 30, 0x02091f, .42));
            const bodyParts = [];
            const addBodyPart = (...args) => {
                const part = this.addPart(...args);
                bodyParts.push(part);
                return part;
            };
            addBodyPart(0, 168, "skin", `tint${skin}_neck.png`, .5, 0).setScale(.72, 1);
            addBodyPart(-58, 218, "shirts", `${shirt}Arm_long.png`, .69, .18).setFlipX(true);
            addBodyPart(58, 218, "shirts", `${shirt}Arm_long.png`, .31, .18);
            addBodyPart(-166, 301, "skin", `tint${skin}_hand.png`, .5, .12);
            addBodyPart(166, 301, "skin", `tint${skin}_hand.png`, .5, .12);
            addBodyPart(-95.5, 341, "skin", `tint${skin}_leg.png`, 0, 0).setFlipX(true);
            addBodyPart(95.5, 341, "skin", `tint${skin}_leg.png`, 1, 0);
            addBodyPart(-95.5, 341, "pants", `${pants}_${length}.png`, 0, 0).setFlipX(true);
            addBodyPart(95.5, 341, "pants", `${pants}_${length}.png`, 1, 0);
            addBodyPart(-66, 505, "shoes", shoe).setFlipX(true).setScale(.86);
            addBodyPart(66, 505, "shoes", shoe).setScale(.86);
            addBodyPart(0, 200, "shirts", shirtFrame, .5, 0);
            addBodyPart(0, 341, "pants", `${pants}${trouserStyle}.png`, .5, 0);
            bodyParts.forEach(part => {
                part.x *= bodyWidth;
                part.scaleX *= bodyWidth;
            });
            this.addPart(0, 35, "skin", `tint${skin}_head.png`, .5, 0).setScale(faceWidth, 1);
            this.addPart(0, 10, "hair", hair, .5, 0);
            this.addPart(-27 * faceWidth, 110, "face", `eye${eyeColour}_${eyeSize}.png`);
            this.addPart(27 * faceWidth, 110, "face", `eye${eyeColour}_${eyeSize}.png`);
            this.addPart(-28 * faceWidth, 90, "face", `${hairPrefix}Brow${browShape}.png`);
            this.addPart(28 * faceWidth, 90, "face", `${hairPrefix}Brow${browShape}.png`).setFlipX(true);
            this.addPart(0, 133, "face", `tint${skin}Nose${noseShape}.png`);
            this.addPart(0, 167, "face", mouth);
        }
    }

    function start(form) {
        if (form.dataset.avatarDesignerReady || typeof Phaser === "undefined") return;
        const host = form.querySelector("[data-avatar-preview]");
        if (!host) return;
        form.dataset.avatarDesignerReady = "true";
        const scene = new AvatarDesignerScene(form, host);
        const game = new Phaser.Game({
            type: Phaser.AUTO,
            parent: host,
            width: 640,
            height: 360,
            transparent: true,
            render: { antialias: true, roundPixels: true },
            scale: { mode: Phaser.Scale.FIT, autoCenter: Phaser.Scale.CENTER_BOTH, width: 640, height: 360 },
            scene
        });
        games.set(form, game);
        setupTabs(form);
        syncHairStyles(form);
        syncShirtStyles(form);
        form.addEventListener("change", event => {
            if (event.target?.name === "presentation") {
                syncHairStyles(form);
                syncShirtStyles(form);
            }
            scene.renderChoice();
        });
    }

    function scan() {
        document.querySelectorAll("[data-avatar-designer]").forEach(start);
        for (const [form, game] of games) {
            if (!form.isConnected) {
                game.destroy(true);
                games.delete(form);
            }
        }
    }

    scan();
    new MutationObserver(scan).observe(document.documentElement, { childList: true, subtree: true });
})();
