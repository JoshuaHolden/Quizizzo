(() => {
    const assetRoot = "/assets/kenney-presenter/spritesheets/";
    const sheets = ["face", "hair", "pants", "shirts", "shoes", "skin"];
    const games = new Map();

    function values(form) {
        return Object.fromEntries(new FormData(form).entries());
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
            const hairPrefix = { Brown: "brown1", Black: "black", Blonde: "blonde", Red: "red" }[choice.hairColour] || "brown1";
            const shirt = { Navy: "navy", Blue: "blue", Green: "green", Red: "red" }[choice.shirtColour] || "navy";
            const pants = { Navy: "pantsNavy", Blue: "pantsBlue1", Green: "pantsGreen", Tan: "pantsTan" }[choice.trouserColour] || "pantsNavy";
            const length = { FullLength: "long", Cropped: "short", Shorts: "shorter" }[choice.trouserLength] || "long";
            const shoe = { Brown: "brownShoe1.png", Black: "blackShoe1.png", Blue: "blueShoe1.png", Red: "redShoe1.png" }[choice.shoeColour] || "brownShoe1.png";
            const hair = `${hairPrefix}${presentation}${presentation === "Man" ? 1 : 3}.png`;
            const shirtFrame = `${shirt}Shirt${presentation === "Woman" ? 4 : 1}.png`;

            this.rig.add(this.add.ellipse(0, 523, 250, 30, 0x02091f, .42));
            this.addPart(0, 168, "skin", `tint${skin}_neck.png`, .5, 0).setScale(.72, 1);
            this.addPart(-58, 218, "shirts", `${shirt}Arm_long.png`, .69, .18).setFlipX(true);
            this.addPart(58, 218, "shirts", `${shirt}Arm_long.png`, .31, .18);
            this.addPart(-166, 301, "skin", `tint${skin}_hand.png`, .5, .12);
            this.addPart(166, 301, "skin", `tint${skin}_hand.png`, .5, .12);
            this.addPart(-95.5, 341, "skin", `tint${skin}_leg.png`, 0, 0).setFlipX(true);
            this.addPart(95.5, 341, "skin", `tint${skin}_leg.png`, 1, 0);
            this.addPart(-95.5, 341, "pants", `${pants}_${length}.png`, 0, 0).setFlipX(true);
            this.addPart(95.5, 341, "pants", `${pants}_${length}.png`, 1, 0);
            this.addPart(-66, 505, "shoes", shoe).setFlipX(true).setScale(.86);
            this.addPart(66, 505, "shoes", shoe).setScale(.86);
            this.addPart(0, 200, "shirts", shirtFrame, .5, 0);
            this.addPart(0, 341, "pants", `${pants}1.png`, .5, 0);
            this.addPart(0, 35, "skin", `tint${skin}_head.png`, .5, 0);
            this.addPart(0, 10, "hair", hair, .5, 0);
            this.addPart(-27, 110, "face", "eyeBlue_large.png");
            this.addPart(27, 110, "face", "eyeBlue_large.png");
            this.addPart(-28, 90, "face", `${hairPrefix}Brow1.png`);
            this.addPart(28, 90, "face", `${hairPrefix}Brow1.png`).setFlipX(true);
            this.addPart(0, 133, "face", `tint${skin}Nose1.png`);
            this.addPart(0, 167, "face", "mouth_happy.png");
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
        form.addEventListener("change", () => scene.renderChoice());
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
