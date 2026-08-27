export const DrawingTool = Object.freeze({
    Pen: "pen",
    Eraser: "eraser"
});

const schemaVersion = 1;
const maximumFrames = 12;
const maximumStrokesPerFrame = 2000;
const maximumPointsPerStroke = 10000;
const maximumTotalPoints = 100000;

function boundedInteger(value, name, minimum, maximum) {
    if (!Number.isInteger(value) || value < minimum || value > maximum) {
        throw new RangeError(`${name} must be an integer from ${minimum} to ${maximum}.`);
    }
    return value;
}

function validateColour(value) {
    return typeof value === "string" && /^#[0-9a-f]{6}$/i.test(value);
}

function normalizeIdentity(identity) {
    if (!identity || typeof identity !== "object") {
        throw new TypeError("A drawing draft identity is required.");
    }
    const normalized = {};
    for (const name of ["partyId", "gameSessionId", "roundId", "playerId"]) {
        const value = identity[name];
        if (typeof value !== "string" || value.length === 0 || value.length > 128) {
            throw new TypeError(`Drawing draft identity '${name}' is invalid.`);
        }
        normalized[name] = value;
    }
    return normalized;
}

function normalizeOptions(options) {
    if (!options || typeof options !== "object") {
        throw new TypeError("Drawing options are required.");
    }
    const colours = [...new Set(options.colours || [])];
    if (colours.length === 0 || colours.length > 32 || !colours.every(validateColour)) {
        throw new TypeError("Drawing colours must contain 1 to 32 six-digit hex colours.");
    }
    const widths = [...new Set(options.widths || [])];
    if (widths.length === 0 || widths.length > 10 ||
        !widths.every(value => Number.isFinite(value) && value >= 1 && value <= 64)) {
        throw new TypeError("Drawing widths must contain 1 to 10 values from 1 to 64.");
    }
    return {
        logicalWidth: boundedInteger(options.logicalWidth, "logicalWidth", 64, 2048),
        logicalHeight: boundedInteger(options.logicalHeight, "logicalHeight", 64, 2048),
        frameCount: boundedInteger(options.frameCount, "frameCount", 1, maximumFrames),
        colours,
        widths,
        identity: normalizeIdentity(options.identity),
        onionSkinEnabled: options.onionSkinEnabled !== false
    };
}

function normalizePoint(point, options) {
    if (!point || !Number.isFinite(point.x) || !Number.isFinite(point.y)) {
        throw new TypeError("A finite drawing point is required.");
    }
    return {
        x: Math.round(Math.min(options.logicalWidth, Math.max(0, point.x)) * 100) / 100,
        y: Math.round(Math.min(options.logicalHeight, Math.max(0, point.y)) * 100) / 100,
        pressure: Number.isFinite(point.pressure)
            ? Math.round(Math.min(1, Math.max(0, point.pressure)) * 1000) / 1000
            : 0.5
    };
}

function sameIdentity(left, right) {
    return left.partyId === right.partyId &&
        left.gameSessionId === right.gameSessionId &&
        left.roundId === right.roundId &&
        left.playerId === right.playerId;
}

export class Stroke {
    constructor(tool, colour, width, firstPoint, options) {
        if (!Object.values(DrawingTool).includes(tool)) {
            throw new TypeError("The drawing tool is invalid.");
        }
        if (!options.colours.includes(colour)) {
            throw new TypeError("The stroke colour is not allowed.");
        }
        if (!options.widths.includes(width)) {
            throw new TypeError("The stroke width is not allowed.");
        }
        this.tool = tool;
        this.colour = colour;
        this.width = width;
        this.points = [normalizePoint(firstPoint, options)];
    }

    addPoint(point, options) {
        if (this.points.length >= maximumPointsPerStroke) {
            return false;
        }
        const normalized = normalizePoint(point, options);
        const previous = this.points[this.points.length - 1];
        const dx = normalized.x - previous.x;
        const dy = normalized.y - previous.y;
        if ((dx * dx) + (dy * dy) < 0.16) {
            return false;
        }
        this.points.push(normalized);
        return true;
    }
}

export class DrawingFrame {
    constructor() {
        this.strokes = [];
    }

    get hasContent() {
        return this.strokes.length > 0;
    }
}

export class DrawingDocument {
    constructor(options) {
        this.options = normalizeOptions(options);
        this.schemaVersion = schemaVersion;
        this.identity = this.options.identity;
        this.logicalWidth = this.options.logicalWidth;
        this.logicalHeight = this.options.logicalHeight;
        this.frameCount = this.options.frameCount;
        this.frames = Array.from({ length: this.frameCount }, () => new DrawingFrame());
        this.currentFrameIndex = 0;
        this.selectedColour = this.options.colours[0];
        this.selectedWidth = this.options.widths[Math.min(1, this.options.widths.length - 1)];
        this.tool = DrawingTool.Pen;
        this.onionSkinEnabled = this.frameCount > 1 && this.options.onionSkinEnabled;
        this.lastUpdatedAt = new Date().toISOString();
        this.activeStroke = null;
        this.totalPoints = 0;
    }

    get currentFrame() {
        return this.frames[this.currentFrameIndex];
    }

    get previousFrame() {
        return this.currentFrameIndex > 0 ? this.frames[this.currentFrameIndex - 1] : null;
    }

    setFrame(index) {
        if (!Number.isInteger(index) || index < 0 || index >= this.frameCount) {
            return false;
        }
        this.endStroke();
        this.currentFrameIndex = index;
        this.touch();
        return true;
    }

    setColour(colour) {
        if (!this.options.colours.includes(colour)) {
            return false;
        }
        this.selectedColour = colour;
        this.touch();
        return true;
    }

    setWidth(width) {
        if (!this.options.widths.includes(width)) {
            return false;
        }
        this.selectedWidth = width;
        this.touch();
        return true;
    }

    setTool(tool) {
        if (!Object.values(DrawingTool).includes(tool)) {
            return false;
        }
        this.endStroke();
        this.tool = tool;
        this.touch();
        return true;
    }

    setOnionSkin(enabled) {
        this.onionSkinEnabled = this.frameCount > 1 && Boolean(enabled);
        this.touch();
    }

    beginStroke(point) {
        this.endStroke();
        if (this.currentFrame.strokes.length >= maximumStrokesPerFrame ||
            this.totalPoints >= maximumTotalPoints) {
            return false;
        }
        this.activeStroke = new Stroke(
            this.tool,
            this.selectedColour,
            this.selectedWidth,
            point,
            this.options);
        this.currentFrame.strokes.push(this.activeStroke);
        this.totalPoints += 1;
        this.touch();
        return true;
    }

    addPoint(point) {
        if (!this.activeStroke) {
            return false;
        }
        if (this.totalPoints >= maximumTotalPoints) {
            return false;
        }
        const added = this.activeStroke.addPoint(point, this.options);
        if (added) {
            this.totalPoints += 1;
            this.touch();
        }
        return added;
    }

    endStroke() {
        const completed = this.activeStroke !== null;
        this.activeStroke = null;
        if (completed) {
            this.touch();
        }
        return completed;
    }

    undo() {
        this.endStroke();
        if (this.currentFrame.strokes.length === 0) {
            return false;
        }
        const removed = this.currentFrame.strokes.pop();
        this.totalPoints -= removed.points.length;
        this.touch();
        return true;
    }

    clearFrame() {
        this.endStroke();
        if (this.currentFrame.strokes.length === 0) {
            return false;
        }
        this.totalPoints -= this.currentFrame.strokes.reduce(
            (count, stroke) => count + stroke.points.length, 0);
        this.currentFrame.strokes = [];
        this.touch();
        return true;
    }

    touch() {
        this.lastUpdatedAt = new Date().toISOString();
    }

    summary(restoredDraft = false) {
        return {
            currentFrame: this.currentFrameIndex + 1,
            frameCount: this.frameCount,
            frameHasContent: this.frames.map(frame => frame.hasContent),
            canUndo: this.currentFrame.hasContent,
            selectedColour: this.selectedColour,
            selectedWidth: this.selectedWidth,
            tool: this.tool,
            onionSkinEnabled: this.onionSkinEnabled,
            restoredDraft,
            lastUpdatedAt: this.lastUpdatedAt
        };
    }

    toJSON() {
        return {
            schemaVersion: this.schemaVersion,
            identity: this.identity,
            logicalWidth: this.logicalWidth,
            logicalHeight: this.logicalHeight,
            frameCount: this.frameCount,
            frames: this.frames.map(frame => ({
                strokes: frame.strokes.map(stroke => ({
                    tool: stroke.tool,
                    colour: stroke.colour,
                    width: stroke.width,
                    points: stroke.points.map(point => ({ ...point }))
                }))
            })),
            currentFrameIndex: this.currentFrameIndex,
            selectedColour: this.selectedColour,
            selectedWidth: this.selectedWidth,
            tool: this.tool,
            onionSkinEnabled: this.onionSkinEnabled,
            lastUpdatedAt: this.lastUpdatedAt
        };
    }

    serialize() {
        return JSON.stringify(this.toJSON());
    }

    static tryRestore(serialized, options) {
        try {
            const data = typeof serialized === "string" ? JSON.parse(serialized) : serialized;
            const document = new DrawingDocument(options);
            if (!data || data.schemaVersion !== schemaVersion ||
                data.logicalWidth !== document.logicalWidth ||
                data.logicalHeight !== document.logicalHeight ||
                data.frameCount !== document.frameCount ||
                !sameIdentity(normalizeIdentity(data.identity), document.identity) ||
                !Array.isArray(data.frames) || data.frames.length !== document.frameCount) {
                return null;
            }

            let totalPoints = 0;
            document.frames = data.frames.map(frameData => {
                if (!frameData || !Array.isArray(frameData.strokes) ||
                    frameData.strokes.length > maximumStrokesPerFrame) {
                    throw new TypeError("The saved drawing frame is invalid.");
                }
                const frame = new DrawingFrame();
                frame.strokes = frameData.strokes.map(strokeData => {
                    if (!strokeData || !Array.isArray(strokeData.points) ||
                        strokeData.points.length === 0 ||
                        strokeData.points.length > maximumPointsPerStroke) {
                        throw new TypeError("The saved drawing stroke is invalid.");
                    }
                    totalPoints += strokeData.points.length;
                    if (totalPoints > maximumTotalPoints) {
                        throw new TypeError("The saved drawing contains too many points.");
                    }
                    const stroke = new Stroke(
                        strokeData.tool,
                        strokeData.colour,
                        strokeData.width,
                        strokeData.points[0],
                        document.options);
                    stroke.points = strokeData.points.map(point =>
                        normalizePoint(point, document.options));
                    return stroke;
                });
                return frame;
            });
            document.totalPoints = totalPoints;

            if (!document.setFrame(data.currentFrameIndex) ||
                !document.setColour(data.selectedColour) ||
                !document.setWidth(data.selectedWidth) ||
                !document.setTool(data.tool)) {
                return null;
            }
            document.setOnionSkin(data.onionSkinEnabled);
            const updatedAt = new Date(data.lastUpdatedAt);
            document.lastUpdatedAt = Number.isNaN(updatedAt.valueOf())
                ? new Date().toISOString()
                : updatedAt.toISOString();
            return document;
        } catch {
            return null;
        }
    }
}
