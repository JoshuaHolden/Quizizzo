(() => {
    const mountedRoots = new Map();
    const reducedMotion = window.matchMedia("(prefers-reduced-motion: reduce)");

    function mount(root) {
        if (mountedRoots.has(root)) {
            return;
        }

        const controller = new AbortController();
        const layers = [...root.querySelectorAll("[data-parallax-layer]")];
        const confetti = [...root.querySelectorAll("[data-scroll-confetti] i")];
        const reveals = [...root.querySelectorAll("[data-reveal]")];
        const rigHost = root.querySelector("[data-landing-character-rig]");
        let pointerX = 0;
        let pointerY = 0;
        let frame = 0;

        const render = () => {
            frame = 0;
            if (reducedMotion.matches) {
                for (const layer of layers) {
                    layer.style.removeProperty("--parallax-transform");
                }
                for (const item of confetti) {
                    item.style.removeProperty("--confetti-x");
                    item.style.removeProperty("--confetti-y");
                    item.style.removeProperty("--confetti-turn");
                }
                return;
            }

            const viewportCentre = window.innerHeight / 2;
            const rootCentre = root.getBoundingClientRect().top + (root.offsetHeight / 2);
            const scrollFactor = Math.max(
                -1,
                Math.min(1, (viewportCentre - rootCentre) / window.innerHeight));

            for (const layer of layers) {
                const depth = Number.parseFloat(layer.dataset.depth ?? "0.5");
                const x = pointerX * 12 * depth;
                const y = (pointerY * 9 + scrollFactor * 18) * depth;
                layer.style.setProperty(
                    "--parallax-transform",
                    `translate3d(${x.toFixed(2)}px, ${y.toFixed(2)}px, 0)`);
            }

            for (const item of confetti) {
                const depth = Number.parseFloat(item.style.getPropertyValue("--depth") || ".5");
                const x = pointerX * 54 * depth;
                const y = pointerY * 38 * depth + window.scrollY * (depth - .58) * .2;
                item.style.setProperty("--confetti-x", `${x.toFixed(2)}px`);
                item.style.setProperty("--confetti-y", `${y.toFixed(2)}px`);
                item.style.setProperty("--confetti-turn", `${(window.scrollY * depth * .14).toFixed(2)}deg`);
            }
        };

        const requestRender = () => {
            if (!frame) {
                frame = window.requestAnimationFrame(render);
            }
        };

        root.addEventListener("pointermove", event => {
            pointerX = (event.clientX / window.innerWidth) - 0.5;
            pointerY = (event.clientY / window.innerHeight) - 0.5;
            requestRender();
        }, { passive: true, signal: controller.signal });

        root.addEventListener("pointerleave", () => {
            pointerX = 0;
            pointerY = 0;
            requestRender();
        }, { passive: true, signal: controller.signal });

        window.addEventListener("scroll", requestRender, { passive: true, signal: controller.signal });
        window.addEventListener("resize", requestRender, { passive: true, signal: controller.signal });
        reducedMotion.addEventListener("change", requestRender, { signal: controller.signal });

        const revealObserver = new IntersectionObserver(entries => {
            for (const entry of entries) {
                if (entry.isIntersecting) {
                    entry.target.classList.add("is-revealing");
                    revealObserver.unobserve(entry.target);
                }
            }
        }, { threshold: 0.14, rootMargin: "0px 0px -6%" });

        for (const reveal of reveals) {
            if (reducedMotion.matches) {
                reveal.classList.remove("is-revealing");
            } else {
                revealObserver.observe(reveal);
            }
        }

        let characterGame = null;
        if (rigHost && window.Phaser && window.quizizzoCharacterRig) {
            const characters = [
                { presentation: "Woman", skinTone: "Tint3", hairColour: "Red", hairStyle: "Style4", shirtColour: "Red", shirtStyle: "Style8", trouserColour: "Navy", shoeColour: "Brown", mouth: "Grin" },
                { presentation: "Man", skinTone: "Tint5", hairColour: "Black", hairStyle: "Style2", shirtColour: "Green", shirtStyle: "Style3", trouserColour: "Blue", shoeColour: "Blue", mouth: "Tongue" },
                { presentation: "Woman", skinTone: "Tint7", hairColour: "Brown", hairStyle: "Style2", shirtColour: "Blue", shirtStyle: "Style4", trouserColour: "Tan", shoeColour: "Red", mouth: "Smile" }
            ];
            const dances = ["armFlap", "fistPump", "discoPoint"];
            const rig = window.quizizzoCharacterRig;
            characterGame = new Phaser.Game({
                type: Phaser.CANVAS,
                parent: rigHost,
                width: 640,
                height: 340,
                transparent: true,
                render: { antialias: true, roundPixels: false },
                scale: { mode: Phaser.Scale.FIT, autoCenter: Phaser.Scale.CENTER_BOTH },
                scene: {
                    preload() { rig.loadAtlases(this, "landing-"); },
                    create() {
                        characters.forEach((character, index) => {
                            const container = this.add.container(150 + index * 170, index === 1 ? 150 : 168)
                                .setScale(index === 1 ? .34 : .3)
                                .setDepth(index === 1 ? 2 : 1);
                            const dancer = rig.create(this, {
                                container,
                                atlasPrefix: "landing-",
                                armsInFront: true,
                                headOffsetY: 18
                            });
                            dancer.render(character, "full");
                            if (reducedMotion.matches) dancer.play("idle");
                            else dancer.play(dances[index], { beatMs: 650 });
                        });
                    }
                }
            });
        }

        controller.signal.addEventListener("abort", () => revealObserver.disconnect(), { once: true });

        mountedRoots.set(root, { controller, characterGame, get frame() { return frame; } });
        requestRender();
    }

    function refresh() {
        for (const [root, state] of mountedRoots) {
            if (!root.isConnected) {
                state.controller.abort();
                if (state.frame) {
                    window.cancelAnimationFrame(state.frame);
                }
                state.characterGame?.destroy(true);
                mountedRoots.delete(root);
            }
        }

        for (const root of document.querySelectorAll("[data-landing-motion]")) {
            mount(root);
        }
    }

    if (document.readyState === "loading") {
        document.addEventListener("DOMContentLoaded", refresh, { once: true });
    } else {
        refresh();
    }
    document.addEventListener("enhancedload", refresh);
    window.addEventListener("pageshow", refresh);
})();
