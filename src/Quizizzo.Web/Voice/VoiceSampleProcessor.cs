using System.Buffers.Binary;

namespace Quizizzo.Web.Voice;

public static class VoiceSampleProcessor
{
    private const float SilenceThreshold = 0.018f;
    private const float TargetPeak = 0.9f;
    private const float TargetRms = 0.2f;
    private const int MaximumChannels = 2;

    public static byte[] CleanPcmWave(ReadOnlySpan<byte> wave)
    {
        var parsed = Parse(wave);
        var first = 0;
        var last = parsed.FrameCount - 1;
        while (first <= last && FramePeak(parsed, first) < SilenceThreshold) first++;
        while (last >= first && FramePeak(parsed, last) < SilenceThreshold) last--;
        if (first > last) throw new InvalidDataException("The recording contains no audible sound.");

        var peak = 0f;
        var sumSquares = 0d;
        var sampleCount = (last - first + 1) * parsed.Channels;
        for (var frame = first; frame <= last; frame++)
        {
            peak = Math.Max(peak, FramePeak(parsed, frame));
            for (var channel = 0; channel < parsed.Channels; channel++)
            {
                var sample = parsed.Samples[frame * parsed.Channels + channel];
                sumSquares += sample * sample;
            }
        }
        var rms = (float)Math.Sqrt(sumSquares / sampleCount);
        var gain = Math.Min(8f, Math.Min(TargetPeak / Math.Max(0.001f, peak),
            TargetRms / Math.Max(0.02f, rms)));
        var outputFrames = last - first + 1;
        var output = new byte[44 + outputFrames * parsed.Channels * 2];
        WriteHeader(output, parsed.SampleRate, parsed.Channels, outputFrames);
        var fadeFrames = Math.Min(parsed.SampleRate / 125, outputFrames / 2);
        var offset = 44;
        for (var frame = 0; frame < outputFrames; frame++)
        {
            var fadeIn = fadeFrames == 0 ? 1f : Math.Min(1f, (float)frame / fadeFrames);
            var fadeOut = fadeFrames == 0 ? 1f : Math.Min(1f, (float)(outputFrames - 1 - frame) / fadeFrames);
            var envelope = Math.Min(fadeIn, fadeOut);
            for (var channel = 0; channel < parsed.Channels; channel++)
            {
                var sample = Math.Clamp(parsed.Samples[(first + frame) * parsed.Channels + channel] * gain * envelope, -1f, 1f);
                BinaryPrimitives.WriteInt16LittleEndian(output.AsSpan(offset, 2),
                    sample < 0 ? (short)(sample * 32768f) : (short)(sample * 32767f));
                offset += 2;
            }
        }
        return output;
    }

    private static WaveData Parse(ReadOnlySpan<byte> wave)
    {
        if (wave.Length < 44 || !wave[..4].SequenceEqual("RIFF"u8) || !wave[8..12].SequenceEqual("WAVE"u8))
            throw new InvalidDataException("The WAV header is invalid.");
        var offset = 12;
        short channels = 0;
        int sampleRate = 0;
        short bits = 0;
        short format = 0;
        ReadOnlySpan<byte> data = default;
        while (offset + 8 <= wave.Length)
        {
            var chunkSize = BinaryPrimitives.ReadInt32LittleEndian(wave[(offset + 4)..]);
            if (chunkSize < 0 || offset + 8 + chunkSize > wave.Length) throw new InvalidDataException("The WAV chunk is invalid.");
            var chunk = wave.Slice(offset + 8, chunkSize);
            if (wave.Slice(offset, 4).SequenceEqual("fmt "u8) && chunk.Length >= 16)
            {
                format = BinaryPrimitives.ReadInt16LittleEndian(chunk);
                channels = BinaryPrimitives.ReadInt16LittleEndian(chunk[2..]);
                sampleRate = BinaryPrimitives.ReadInt32LittleEndian(chunk[4..]);
                bits = BinaryPrimitives.ReadInt16LittleEndian(chunk[14..]);
            }
            else if (wave.Slice(offset, 4).SequenceEqual("data"u8)) data = chunk;
            offset += 8 + chunkSize + (chunkSize & 1);
        }
        if (format != 1 || channels is < 1 or > MaximumChannels || sampleRate <= 0 || bits != 16 || data.Length == 0)
            throw new InvalidDataException("Only PCM 16-bit WAV recordings with one or two channels are supported.");
        var frameBytes = channels * 2;
        if (data.Length % frameBytes != 0) throw new InvalidDataException("The WAV sample data is incomplete.");
        var samples = new float[data.Length / 2];
        for (var index = 0; index < samples.Length; index++)
            samples[index] = BinaryPrimitives.ReadInt16LittleEndian(data[(index * 2)..]) / 32768f;
        return new WaveData(channels, sampleRate, samples);
    }

    private static float FramePeak(WaveData wave, int frame)
    {
        var peak = 0f;
        for (var channel = 0; channel < wave.Channels; channel++)
            peak = Math.Max(peak, Math.Abs(wave.Samples[frame * wave.Channels + channel]));
        return peak;
    }

    private static void WriteHeader(Span<byte> output, int sampleRate, short channels, int frames)
    {
        "RIFF"u8.CopyTo(output); BinaryPrimitives.WriteInt32LittleEndian(output[4..], output.Length - 8);
        "WAVE"u8.CopyTo(output[8..]); "fmt "u8.CopyTo(output[12..]);
        BinaryPrimitives.WriteInt32LittleEndian(output[16..], 16);
        BinaryPrimitives.WriteInt16LittleEndian(output[20..], 1);
        BinaryPrimitives.WriteInt16LittleEndian(output[22..], channels);
        BinaryPrimitives.WriteInt32LittleEndian(output[24..], sampleRate);
        BinaryPrimitives.WriteInt32LittleEndian(output[28..], sampleRate * channels * 2);
        BinaryPrimitives.WriteInt16LittleEndian(output[32..], (short)(channels * 2));
        BinaryPrimitives.WriteInt16LittleEndian(output[34..], 16);
        "data"u8.CopyTo(output[36..]); BinaryPrimitives.WriteInt32LittleEndian(output[40..], frames * channels * 2);
    }

    private sealed record WaveData(short Channels, int SampleRate, float[] Samples)
    {
        public int FrameCount => Samples.Length / Channels;
    }
}
