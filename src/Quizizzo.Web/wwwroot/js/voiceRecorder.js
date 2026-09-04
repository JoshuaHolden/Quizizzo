const silenceThreshold = 0.018;
const fadeSeconds = 0.008;

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
    const trimmed = channels.map(channel => channel.slice(start, end));
    let peak = 0;
    for (const channel of trimmed) {
        for (const sample of channel) {
            peak = Math.max(peak, Math.abs(sample));
        }
    }
    const gain = peak > 0 ? Math.min(8, 0.9 / peak) : 1;
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

    async function notify(key, hasRecording, status) {
        await dotNet.invokeMethodAsync("RecordingChanged", key, hasRecording, status);
    }

    async function ensureStream() {
        if (!navigator.mediaDevices?.getUserMedia || !globalThis.MediaRecorder) {
            throw new Error("This browser cannot record microphone audio.");
        }
        if (!stream || stream.getTracks().every(track => track.readyState === "ended")) {
            stream = await navigator.mediaDevices.getUserMedia({
                audio: { echoCancellation: false, noiseSuppression: false, autoGainControl: false },
                video: false
            });
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
    }

    return { start, stop, play, upload, dispose };
}