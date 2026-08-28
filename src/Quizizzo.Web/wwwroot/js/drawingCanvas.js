import { DrawingDocument, DrawingTool } from "./drawingDocument.mjs";

const controllers = new WeakMap();

export class DrawingDraftStore {
    constructor(storage, key) {
        this.storage = storage;
        this.key = key;
        this.disabled = false;
        this.submissionKey = `${key}:submission-id`;
    }

    load() {
        try {
            return this.storage?.getItem(this.key) ?? null;
        } catch {
            return null;
        }
    }

    save(serialized) {
        if (this.disabled) {
            return;
        }
        try {
            this.storage?.setItem(this.key, serialized);
        } catch {
            // Quota and privacy failures do not make the live canvas unusable.
        }
    }

    removeObsolete(prefix, playerSuffix) {
        if (!this.storage || !prefix || !playerSuffix) {
            return;
        }
        try {
            const obsoleteKeys = [];
            for (let index = 0; index < this.storage.length; index += 1) {
                const key = this.storage.key(index);
                const belongsToPlayer = key && (
                    key.endsWith(playerSuffix) ||
                    key.endsWith(`${playerSuffix}:submission-id`));
                if (key && key !== this.key && key !== this.submissionKey &&
                    key.startsWith(prefix) && belongsToPlayer) {
                    obsoleteKeys.push(key);
                }
            }
            for (const key of obsoleteKeys) {
                this.storage.removeItem(key);
            }
        } catch {
            // Storage can be disabled; drawing still works for the current page lifetime.
        }
    }

    clear() {
        this.disabled = true;
        try {
            this.storage?.removeItem(this.key);
            this.storage?.removeItem(this.submissionKey);
        } catch {
            // The submission can still complete when browser storage is unavailable.
        }
    }

    getOrCreateSubmissionId(createId) {
        try {
            const existing = this.storage?.getItem(this.submissionKey);
            if (existing && /^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i.test(existing)) {
                return existing;
            }
            const created = createId();
            this.storage?.setItem(this.submissionKey, created);
            return created;
        } catch {
            return createId();
        }
    }
}

class CanvasDrawingController {
    constructor(drawingCanvas, onionCanvas, options, dotNetReference) {
        this.drawingCanvas = drawingCanvas;
        this.onionCanvas = onionCanvas;
        this.dotNetReference = dotNetReference;
        this.draftKey = options.draftKey;
        this.activePointerId = null;
        this.saveTimer = null;
        this.renderFrameHandle = null;
        this.disposed = false;

        let storage = null;
        try {
            storage = window.localStorage;
        } catch { }
        this.draftStore = new DrawingDraftStore(storage, this.draftKey);
        this.submissionId = this.draftStore.getOrCreateSubmissionId(() => crypto.randomUUID());
        this.draftStore.removeObsolete(options.draftFamilyPrefix, options.draftPlayerSuffix);
        const restored = DrawingDocument.tryRestore(this.draftStore.load(), options);
        this.document = restored || new DrawingDocument(options);

        this.onPointerDown = event => this.pointerDown(event);
        this.onPointerMove = event => this.pointerMove(event);
        this.onPointerUp = event => this.pointerUp(event);
        this.onResize = () => this.resize();
        drawingCanvas.addEventListener("pointerdown", this.onPointerDown);
        drawingCanvas.addEventListener("pointermove", this.onPointerMove);
        drawingCanvas.addEventListener("pointerup", this.onPointerUp);
        drawingCanvas.addEventListener("pointercancel", this.onPointerUp);
        drawingCanvas.addEventListener("lostpointercapture", this.onPointerUp);
        this.resizeObserver = new ResizeObserver(this.onResize);
        this.resizeObserver.observe(drawingCanvas);
        this.resize();
        this.notify(Boolean(restored));
    }

    resize() {
        const ratio = Math.min(3, Math.max(1, window.devicePixelRatio || 1));
        for (const canvas of [this.drawingCanvas, this.onionCanvas]) {
            const width = Math.round(this.document.logicalWidth * ratio);
            const height = Math.round(this.document.logicalHeight * ratio);
            if (canvas.width !== width || canvas.height !== height) {
                canvas.width = width;
                canvas.height = height;
            }
            const context = canvas.getContext("2d");
            context.setTransform(ratio, 0, 0, ratio, 0, 0);
            context.lineCap = "round";
            context.lineJoin = "round";
        }
        this.render();
    }

    logicalPoint(event) {
        const bounds = this.drawingCanvas.getBoundingClientRect();
        return {
            x: (event.clientX - bounds.left) * this.document.logicalWidth / bounds.width,
            y: (event.clientY - bounds.top) * this.document.logicalHeight / bounds.height,
            pressure: event.pressure > 0 ? event.pressure : 0.5
        };
    }

    pointerDown(event) {
        if (this.activePointerId !== null || (event.pointerType === "mouse" && event.button !== 0)) {
            return;
        }
        event.preventDefault();
        this.activePointerId = event.pointerId;
        this.drawingCanvas.setPointerCapture(event.pointerId);
        this.document.beginStroke(this.logicalPoint(event));
        this.renderCurrentFrameSoon();
    }

    pointerMove(event) {
        if (event.pointerId !== this.activePointerId) {
            return;
        }
        event.preventDefault();
        const events = typeof event.getCoalescedEvents === "function"
            ? event.getCoalescedEvents()
            : [event];
        let changed = false;
        for (const sample of events) {
            changed = this.document.addPoint(this.logicalPoint(sample)) || changed;
        }
        if (changed) {
            this.renderCurrentFrameSoon();
        }
    }

    pointerUp(event) {
        if (event.pointerId !== this.activePointerId) {
            return;
        }
        event.preventDefault();
        if (event.type === "pointerup") {
            this.document.addPoint(this.logicalPoint(event));
        }
        this.document.endStroke();
        this.activePointerId = null;
        if (this.drawingCanvas.hasPointerCapture(event.pointerId)) {
            this.drawingCanvas.releasePointerCapture(event.pointerId);
        }
        this.renderCurrentFrameSoon();
        this.persistSoon();
        this.notify(false);
    }

    render() {
        if (this.renderFrameHandle !== null) {
            window.cancelAnimationFrame(this.renderFrameHandle);
            this.renderFrameHandle = null;
        }
        this.renderOnionFrame();
        this.renderCurrentFrame();
    }

    renderCurrentFrameSoon() {
        if (this.renderFrameHandle !== null) {
            return;
        }
        this.renderFrameHandle = window.requestAnimationFrame(() => {
            this.renderFrameHandle = null;
            this.renderCurrentFrame();
        });
    }

    renderOnionFrame() {
        const context = this.onionCanvas.getContext("2d");
        this.clear(context, this.onionCanvas);
        if (!this.document.onionSkinEnabled || !this.document.previousFrame) {
            return;
        }
        context.save();
        context.globalAlpha = 0.2;
        this.renderFrame(context, this.document.previousFrame, true);
        context.restore();
    }

    renderCurrentFrame() {
        const context = this.drawingCanvas.getContext("2d");
        this.clear(context, this.drawingCanvas);
        this.renderFrame(context, this.document.currentFrame, true);
    }

    clear(context, canvas) {
        context.save();
        context.setTransform(1, 0, 0, 1, 0, 0);
        context.clearRect(0, 0, canvas.width, canvas.height);
        context.restore();
    }

    renderFrame(context, frame, allowEraser) {
        for (const stroke of frame.strokes) {
            context.save();
            context.globalCompositeOperation = allowEraser && stroke.tool === DrawingTool.Eraser
                ? "destination-out"
                : "source-over";
            if (allowEraser && stroke.tool === DrawingTool.Eraser) {
                context.globalAlpha = 1;
            }
            context.strokeStyle = stroke.colour;
            context.fillStyle = stroke.colour;
            context.lineWidth = stroke.width;
            if (stroke.points.length === 1) {
                context.beginPath();
                context.arc(stroke.points[0].x, stroke.points[0].y, stroke.width / 2, 0, Math.PI * 2);
                context.fill();
            } else {
                context.beginPath();
                context.moveTo(stroke.points[0].x, stroke.points[0].y);
                for (let index = 1; index < stroke.points.length; index += 1) {
                    context.lineTo(stroke.points[index].x, stroke.points[index].y);
                }
                context.stroke();
            }
            context.restore();
        }
    }

    setFrame(index) {
        if (this.document.setFrame(index)) {
            this.changed();
        }
    }

    setColour(colour) {
        if (this.document.setColour(colour)) {
            this.document.setTool(DrawingTool.Pen);
            this.changed();
        }
    }

    setWidth(width) {
        if (this.document.setWidth(width)) {
            this.changed();
        }
    }

    setTool(tool) {
        if (this.document.setTool(tool)) {
            this.changed();
        }
    }

    setOnionSkin(enabled) {
        this.document.setOnionSkin(enabled);
        this.changed();
    }

    undo() {
        if (this.document.undo()) {
            this.changed();
        }
    }

    clearFrame() {
        if (this.document.clearFrame()) {
            this.changed();
        }
    }

    changed() {
        this.render();
        this.persistSoon();
        this.notify(false);
    }

    persistSoon() {
        clearTimeout(this.saveTimer);
        this.saveTimer = setTimeout(() => this.persist(), 150);
    }

    persist() {
        clearTimeout(this.saveTimer);
        this.saveTimer = null;
        this.draftStore.save(this.document.serialize());
    }

    clearDraft() {
        clearTimeout(this.saveTimer);
        this.saveTimer = null;
        this.draftStore.clear();
    }

    getDocument() {
        this.document.endStroke();
        return this.document.toJSON();
    }

    async submit(endpoint, request) {
        if (!endpoint || !request?.gameInstanceId || !request?.roundId) {
            throw new Error("Drawing submission details are incomplete.");
        }
        const form = new FormData();
        form.set("gameInstanceId", request.gameInstanceId);
        form.set("roundId", request.roundId);
        form.set("commandId", this.submissionId);
        for (let index = 0; index < this.document.frames.length; index += 1) {
            const blob = await this.exportFrame(this.document.frames[index]);
            form.append("frames", blob, `frame-${index + 1}.png`);
        }
        const response = await fetch(endpoint, {
            method: "POST",
            credentials: "same-origin",
            body: form,
            headers: { "X-Requested-With": "QuizizzoDrawingController" }
        });
        if (!response.ok) {
            const message = await response.text();
            throw new Error(message || "The animation could not be submitted.");
        }
        const result = await response.json();
        this.clearDraft();
        return result;
    }

    exportFrame(frame) {
        const canvas = document.createElement("canvas");
        canvas.width = this.document.logicalWidth;
        canvas.height = this.document.logicalHeight;
        const context = canvas.getContext("2d");
        context.lineCap = "round";
        context.lineJoin = "round";
        this.renderFrame(context, frame, true);
        context.save();
        context.globalCompositeOperation = "destination-over";
        context.fillStyle = "#ffffff";
        context.fillRect(0, 0, canvas.width, canvas.height);
        context.restore();
        return new Promise((resolve, reject) => canvas.toBlob(
            blob => blob ? resolve(blob) : reject(new Error("A drawing frame could not be encoded.")),
            "image/png"));
    }

    notify(restoredDraft) {
        if (this.disposed) {
            return;
        }
        this.dotNetReference.invokeMethodAsync(
            "HandleDrawingStateChanged",
            this.document.summary(restoredDraft)).catch(() => { });
    }

    dispose() {
        if (this.disposed) {
            return;
        }
        this.persist();
        this.disposed = true;
        this.resizeObserver.disconnect();
        if (this.renderFrameHandle !== null) {
            window.cancelAnimationFrame(this.renderFrameHandle);
            this.renderFrameHandle = null;
        }
        this.drawingCanvas.removeEventListener("pointerdown", this.onPointerDown);
        this.drawingCanvas.removeEventListener("pointermove", this.onPointerMove);
        this.drawingCanvas.removeEventListener("pointerup", this.onPointerUp);
        this.drawingCanvas.removeEventListener("pointercancel", this.onPointerUp);
        this.drawingCanvas.removeEventListener("lostpointercapture", this.onPointerUp);
        this.dotNetReference = null;
    }
}

export function create(drawingCanvas, onionCanvas, options, dotNetReference) {
    const controller = new CanvasDrawingController(drawingCanvas, onionCanvas, options, dotNetReference);
    controllers.set(drawingCanvas, controller);
    return controller;
}
