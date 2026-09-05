const silenceThreshold = 0.018;
const fadeSeconds = 0.015;

export function findAudibleBounds(channelData, threshold = silenceThreshold) {
    let first = 0;
    while (first < channelData.length && Math.abs(channelData[first]) < threshold) {
        first += 1;
    }
    let last = channelData.length - 1;
    while (last > first && Math.abs(channelData[last]) < threshold) {
        last -= 1;
    }
    return first >= channelData.length ? { start: 0, end: channelData.length } : { start: first, end: last + 1 };
}

export function normalizeAndFade(channels, sampleRate) {
    const bounds = channels.map(channel => findAudibleBounds(channel));
    const start = Math.min(...bounds.map(item => item.start));
    const end = Math.max(...bounds.map(item => item.end));
    const trimmed = channels.map(channel => {
        const source = channel.slice(start, end);
        const mean = source.reduce((sum, sample) => sum + sample, 0) / Math.max(1, source.length);
        return Float32Array.from(source, sample => {
            const centred = sample - mean;
            return Math.abs(centred) < silenceThreshold * 0.65 ? 0 : centred;
        });
    });
    let peak = 0;
    let sumSquares = 0;
    let sampleCount = 0;
    for (const channel of trimmed) {
        for (const sample of channel) {
            peak = Math.max(peak, Math.abs(sample));
            sumSquares += sample * sample;
            sampleCount += 1;
        }
    }
    const rms = Math.sqrt(sumSquares / Math.max(1, sampleCount));
    // Aim for a consistent voice level while refusing to turn room noise into an instrument.
    const gain = peak > 0 ? Math.min(3, 0.88 / peak, 0.18 / Math.max(0.025, rms)) : 1;
    const fadeSamples = Math.min(Math.floor(sampleRate * fadeSeconds), Math.floor((end - start) / 2));
    return trimmed.map(channel => {
        const output = new Float32Array(channel.length);
        for (let index = 0; index < channel.length; index += 1) {
            const attack = fadeSamples === 0 ? 1 : Math.min(1, index / fadeSamples);
            const release = fadeSamples === 0 ? 1 : Math.min(1, (channel.length - 1 - index) / fadeSamples);
            output[index] = Math.max(-1, Math.min(1, channel[index] * gain * attack * release));
        }
        return output;
    });
}

export function encodeWave(channels, sampleRate) {
    const frameCount = channels[0]?.length ?? 0;
    const channelCount = channels.length;
    const bytesPerSample = 2;
    const buffer = new ArrayBuffer(44 + frameCount * channelCount * bytesPerSample);
    const view = new DataView(buffer);
    const write = (offset, value) => [...value].forEach((character, index) => view.setUint8(offset + index, character.charCodeAt(0)));
    write(0, "RIFF");
    view.setUint32(4, buffer.byteLength - 8, true);
    write(8, "WAVE");
    write(12, "fmt ");
    view.setUint32(16, 16, true);
    view.setUint16(20, 1, true);
    view.setUint16(22, channelCount, true);
    view.setUint32(24, sampleRate, true);
    view.setUint32(28, sampleRate * channelCount * bytesPerSample, true);
    view.setUint16(32, channelCount * bytesPerSample, true);
    view.setUint16(34, 16, true);
    write(36, "data");
    view.setUint32(40, buffer.byteLength - 44, true);
    let offset = 44;
    for (let frame = 0; frame < frameCount; frame += 1) {
        for (let channel = 0; channel < channelCount; channel += 1) {
            const sample = Math.max(-1, Math.min(1, channels[channel][frame]));
            view.setInt16(offset, sample < 0 ? sample * 0x8000 : sample * 0x7fff, true);
            offset += bytesPerSample;
        }
    }
    return new Blob([buffer], { type: "audio/wav" });
}

async function processBlob(blob) {
    const AudioContextType = globalThis.AudioContext ?? globalThis.webkitAudioContext;
    if (!AudioContextType) {
        return blob;
    }
    const context = new AudioContextType();
    try {
        const decoded = await context.decodeAudioData(await blob.arrayBuffer());
        const channels = Array.from({ length: decoded.numberOfChannels }, (_, index) => decoded.getChannelData(index));
        return encodeWave(normalizeAndFade(channels, decoded.sampleRate), decoded.sampleRate);
    } finally {
        await context.close();
    }
}

function unlockVoiceAudio() {
    const AudioContextType = globalThis.AudioContext ?? globalThis.webkitAudioContext;
    if (!AudioContextType) return;
    if (!globalThis.quizizzoVoiceAudioContext || globalThis.quizizzoVoiceAudioContext.state === "closed") {
        globalThis.quizizzoVoiceAudioContext = new AudioContextType({ latencyHint: "interactive" });
    }
    if (globalThis.quizizzoVoiceAudioContext.state === "suspended") {
        void globalThis.quizizzoVoiceAudioContext.resume();
    }
}

export function createVoiceRecorder(dotNet, maximumDurationSeconds, maximumBytesPerSample) {
    let stream = null;
    let active = null;
    let stopTimer = null;
    const samples = new Map();
    const urls = new Map();
    const commandIds = new Map();
    let hasRequestedMicrophone = false;

    async function notify(key, hasRecording, status) {
        await dotNet.invokeMethodAsync("RecordingChanged", key, hasRecording, status);
    }

    async function ensureStream() {
        if (!globalThis.isSecureContext) {
            throw new Error("MICROPHONE_UNAVAILABLE|Microphone recording requires HTTPS. Open Quizizzo using its secure address.");
        }
        if (!navigator.mediaDevices?.getUserMedia || !globalThis.MediaRecorder) {
            throw new Error("MICROPHONE_UNAVAILABLE|This browser cannot record microphone audio.");
        }
        if (!stream || stream.getTracks().every(track => track.readyState === "ended")) {
            try {
                hasRequestedMicrophone = true;
                stream = await navigator.mediaDevices.getUserMedia({
                    audio: { echoCancellation: false, noiseSuppression: false, autoGainControl: false },
                    video: false
                });
            } catch (error) {
                if (error?.name === "NotAllowedError" || error?.name === "PermissionDeniedError") {
                    throw new Error("MICROPHONE_BLOCKED");
                }
                if (error?.name === "NotFoundError" || error?.name === "DevicesNotFoundError")
                    throw new Error("MICROPHONE_UNAVAILABLE|No microphone was found on this device.");
                if (error?.name === "NotReadableError" || error?.name === "AbortError")
                    throw new Error("MICROPHONE_UNAVAILABLE|Your microphone is busy in another app. Close it and try again.");
                throw error;
            }
        }
        return stream;
    }

    async function start(key) {
        if (active) {
            throw new Error("Finish the current recording first.");
        }
        unlockVoiceAudio();
        const mediaStream = await ensureStream();
        const chunks = [];
        const recorder = new MediaRecorder(mediaStream);
        active = { key, recorder, chunks };
        recorder.addEventListener("dataavailable", event => {
            if (event.data.size > 0) chunks.push(event.data);
        });
        recorder.addEventListener("stop", async () => {
            clearTimeout(stopTimer);
            try {
                const processed = await processBlob(new Blob(chunks, { type: recorder.mimeType || "audio/webm" }));
                if (processed.size <= 0 || processed.size > maximumBytesPerSample) {
                    throw new Error("That recording is too large. Try a shorter sound.");
                }
                samples.set(key, processed);
                if (urls.has(key)) URL.revokeObjectURL(urls.get(key));
                urls.set(key, URL.createObjectURL(processed));
                await notify(key, true, "ready");
            } catch (error) {
                await notify(key, false, "idle");
                console.error("VoiceChoon recording processing failed", error);
            } finally {
                active = null;
            }
        }, { once: true });
        recorder.start(100);
        stopTimer = setTimeout(() => {
            if (recorder.state === "recording") recorder.stop();
        }, maximumDurationSeconds * 1000);
        await notify(key, false, "recording");
    }

    function stop(key) {
        if (active?.key === key && active.recorder.state === "recording") {
            active.recorder.stop();
        }
    }

    function play(key) {
        const url = urls.get(key);
        if (!url) throw new Error("Record this sound before playing it.");
        const audio = new Audio(url);
        audio.play().catch(() => { });
    }

    async function upload(key, endpoint, gameInstanceId) {
        const sample = samples.get(key);
        if (!sample) throw new Error("Record this sound before using it.");
        const commandId = commandIds.get(key) ?? crypto.randomUUID();
        commandIds.set(key, commandId);
        const form = new FormData();
        form.append("gameInstanceId", gameInstanceId);
        form.append("commandId", commandId);
        form.append("promptKey", key);
        form.append("sample", sample, `voicechoon-${key}.wav`);
        const antiforgeryToken = document.querySelector('input[name="__RequestVerificationToken"]')?.value;
        if (!antiforgeryToken) throw new Error("The secure upload token is unavailable. Reload Quizizzo.");
        form.append("__RequestVerificationToken", antiforgeryToken);
        const response = await fetch(endpoint, {
            method: "POST",
            credentials: "same-origin",
            headers: { "X-Requested-With": "QuizizzoVoiceController" },
            body: form
        });
        const payload = await response.json().catch(() => null);
        if (!response.ok) {
            throw new Error(typeof payload === "string" ? payload : "That sound could not be saved.");
        }
        return payload.assetId;
    }

    function dispose() {
        clearTimeout(stopTimer);
        if (active?.recorder.state === "recording") active.recorder.stop();
        for (const url of urls.values()) URL.revokeObjectURL(url);
        for (const track of stream?.getTracks() ?? []) track.stop();
        samples.clear();
        urls.clear();
        globalThis.removeEventListener("focus", recheckPermission);
        document.removeEventListener("visibilitychange", recheckPermission);
    }

    async function recheckPermission() {
        if (!hasRequestedMicrophone || document.visibilityState === "hidden") return;
        await dotNet.invokeMethodAsync("MicrophonePermissionRechecked", await microphonePermissionState());
    }

    globalThis.addEventListener("focus", recheckPermission);
    document.addEventListener("visibilitychange", recheckPermission);

    return { start, stop, play, upload, dispose };
}

export function microphoneEnvironment() {
    const agent = navigator.userAgent || "";
    const ios = /iPad|iPhone|iPod/.test(agent)
        || (navigator.platform === "MacIntel" && navigator.maxTouchPoints > 1);
    if (!ios) return "other";
    return /Brave/i.test(agent) || Boolean(navigator.brave) ? "brave-ios" : "ios";
}

export async function microphonePermissionState() {
    if (!globalThis.isSecureContext || !navigator.mediaDevices?.getUserMedia) return "unavailable";
    try {
        if (navigator.permissions?.query) {
            const permission = await navigator.permissions.query({ name: "microphone" });
            if (["granted", "prompt", "denied"].includes(permission.state)) return permission.state;
        }
    } catch {
        // Safari and older browsers may support microphone capture without exposing
        // the microphone descriptor through the Permissions API.
    }
    try {
        const devices = await navigator.mediaDevices.enumerateDevices?.();
        if (devices?.some(device => device.kind === "audioinput" && device.label)) return "granted";
    } catch { }
    return "unknown";
}
