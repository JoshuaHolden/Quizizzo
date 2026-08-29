(() => {
    const mountedRoots = new Map();
    const reducedMotion = window.matchMedia("(prefers-reduced-motion: reduce)");

    function mount(root) {
        if (mountedRoots.has(root)) {
            return;
        }

        const controller = new AbortController();
        const layers = [...root.querySelectorAll("[data-parallax-layer]")];
        let pointerX = 0;
        let pointerY = 0;
        let frame = 0;

        const render = () => {
            frame = 0;
            if (reducedMotion.matches) {
                for (const layer of layers) {
                    layer.style.removeProperty("--parallax-transform");
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
