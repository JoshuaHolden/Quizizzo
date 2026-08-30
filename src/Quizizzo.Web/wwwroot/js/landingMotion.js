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
                    item.style.removeProperty("--confetti-shift");
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

            const scrollProgress = window.scrollY / Math.max(1, document.documentElement.scrollHeight - window.innerHeight);
            for (const item of confetti) {
                const drift = Number.parseFloat(item.style.getPropertyValue("--drift") || "1");
                item.style.setProperty("--confetti-shift", `${(scrollProgress * 150 * drift).toFixed(2)}px`);
                item.style.setProperty("--confetti-turn", `${(scrollProgress * 240 * drift).toFixed(2)}deg`);
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
                    entry.target.classList.add("is-visible");
                    revealObserver.unobserve(entry.target);
                }
            }
        }, { threshold: 0.14, rootMargin: "0px 0px -6%" });

        for (const reveal of reveals) {
            if (reducedMotion.matches) {
                reveal.classList.add("is-visible");
            } else {
                revealObserver.observe(reveal);
            }
        }

        controller.signal.addEventListener("abort", () => revealObserver.disconnect(), { once: true });

        mountedRoots.set(root, { controller, get frame() { return frame; } });
        requestRender();
    }

    function refresh() {
        for (const [root, state] of mountedRoots) {
            if (!root.isConnected) {
                state.controller.abort();
                if (state.frame) {
                    window.cancelAnimationFrame(state.frame);
                }
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
})();
