import { mkdirSync, writeFileSync } from "node:fs";
import { dirname, resolve } from "node:path";

const outputPath = resolve("src/Quizizzo.Web/wwwroot/assets/audio/voicechoon-countdown-blip.wav");
const sampleRate = 44_100;
const durationSeconds = 0.14;
const sampleCount = Math.round(sampleRate * durationSeconds);
const dataSize = sampleCount * 2;
const wav = Buffer.alloc(44 + dataSize);

wav.write("RIFF", 0);
wav.writeUInt32LE(36 + dataSize, 4);
wav.write("WAVEfmt ", 8);
wav.writeUInt32LE(16, 16);
wav.writeUInt16LE(1, 20);
wav.writeUInt16LE(1, 22);
wav.writeUInt32LE(sampleRate, 24);
wav.writeUInt32LE(sampleRate * 2, 28);
wav.writeUInt16LE(2, 32);
wav.writeUInt16LE(16, 34);
wav.write("data", 36);
wav.writeUInt32LE(dataSize, 40);

for (let index = 0; index < sampleCount; index += 1) {
    const time = index / sampleRate;
    const attack = Math.min(1, time / 0.008);
    const release = Math.pow(Math.max(0, 1 - time / durationSeconds), 2.6);
    const tone = Math.sin(2 * Math.PI * 880 * time) * 0.78
        + Math.sin(2 * Math.PI * 1320 * time) * 0.22;
    wav.writeInt16LE(Math.round(32_767 * 0.42 * attack * release * tone), 44 + index * 2);
}

mkdirSync(dirname(outputPath), { recursive: true });
writeFileSync(outputPath, wav);
console.log(`Wrote ${outputPath} (${wav.length} bytes)`);
