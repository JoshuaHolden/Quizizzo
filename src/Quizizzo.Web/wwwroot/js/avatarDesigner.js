(() => {
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
            window.quizizzoCharacterRig.loadAtlases(this, "designer-");
        }

        create() {
            this.rig = this.add.container(320, 30).setScale(.58);
            this.character = window.quizizzoCharacterRig.create(this, {
                container: this.rig,
                atlasPrefix: "designer-",
                includeGroundShadow: true
            });
            this.renderChoice();
            this.scheduleRareFart();
            this.events.once(Phaser.Scenes.Events.SHUTDOWN, () => {
                this.fartTimer?.remove(false);
                this.character?.destroy();
            });
        }

        renderChoice() {
            if (!this.character) return;
            this.character.render(values(this.form), "full");
            this.character.play("idle");
        }

        scheduleRareFart() {
            this.fartTimer?.remove(false);
            if (window.matchMedia("(prefers-reduced-motion: reduce)").matches) return;
            this.fartTimer = this.time.delayedCall(Phaser.Math.Between(30000, 55000), () => {
                this.character.play("fart", {
                    resumeIdle: true,
                    onComplete: () => this.scheduleRareFart()
                });
            });
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
