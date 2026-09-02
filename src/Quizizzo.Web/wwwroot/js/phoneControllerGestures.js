(() => {
    const controllerFor = target => {
        const element = target instanceof Element ? target : target instanceof Node ? target.parentElement : null;
        return element?.closest(".phone-controller-shell");
    };

    const preventControllerGesture = event => {
        if (controllerFor(event.target) && event.cancelable) {
            event.preventDefault();
        }
    };

    // Safari exposes pinch zoom through gesture events even when a fixed controller
    // already declares touch-action:none. Keep the guard route-scoped.
    for (const eventName of ["gesturestart", "gesturechange", "gestureend", "dblclick"]) {
        document.addEventListener(eventName, preventControllerGesture, { passive: false });
    }
})();
