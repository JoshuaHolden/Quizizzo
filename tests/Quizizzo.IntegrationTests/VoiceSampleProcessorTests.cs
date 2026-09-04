using System.Buffers.Binary;
using Quizizzo.Web.Voice;

namespace Quizizzo.IntegrationTests;

public sealed class VoiceSampleProcessorTests
{
    [Fact]
    public void Cleans_silence_normalizes_peak_and_adds_fades()
    {
        var input = Wave([0f, 0f, 0.25f, 0.5f, 0.25f, 0f, 0f], 1000);

        var output = VoiceSampleProcessor.CleanPcmWave(input);
        var samples = ReadSamples(output);

        Assert.Equal(3, samples.Length);
        Assert.InRange(samples[0], 0, 100);
        Assert.InRange(samples[^1], -100, 0);
        Assert.InRange(samples[1], 9000, 10000);
    }

    [Fact]
    public void Rejects_recordings_without_a_valid_audible_pcm_wave()
    {
        Assert.Throws<InvalidDataException>(() => VoiceSampleProcessor.CleanPcmWave(Wave([0f, 0f], 16000)));
        Assert.Throws<InvalidDataException>(() => VoiceSampleProcessor.CleanPcmWave("bad"u8));
    }

    private static byte[] Wave(float[] samples, int sampleRate)
    {
        var output = new byte[44 + samples.Length * 2];
        "RIFF"u8.CopyTo(output); BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(4), output.Length - 8);
        "WAVE"u8.CopyTo(output.AsSpan(8)); "fmt "u8.CopyTo(output.AsSpan(12));
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(16), 16);
        BinaryPrimitives.WriteInt16LittleEndian(output.AsSpan(20), 1);
        BinaryPrimitives.WriteInt16LittleEndian(output.AsSpan(22), 1);
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(24), sampleRate);
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(28), sampleRate * 2);
        BinaryPrimitives.WriteInt16LittleEndian(output.AsSpan(32), 2);
        BinaryPrimitives.WriteInt16LittleEndian(output.AsSpan(34), 16);
        "data"u8.CopyTo(output.AsSpan(36)); BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(40), samples.Length * 2);
        for (var index = 0; index < samples.Length; index++)
            BinaryPrimitives.WriteInt16LittleEndian(output.AsSpan(44 + index * 2), (short)(samples[index] * 32767));
        return output;
    }

    private static short[] ReadSamples(byte[] wave)
    {
        var samples = new short[(wave.Length - 44) / 2];
        for (var index = 0; index < samples.Length; index++)
            samples[index] = BinaryPrimitives.ReadInt16LittleEndian(wave.AsSpan(44 + index * 2));
        return samples;
    }
}
