using Quizizzo.Domain.Voice;

namespace Quizizzo.Domain.Tests;

public sealed class VoiceChoonReplayTests
{
    [Fact]
    public void Replay_is_permanent_compact_and_deduplicates_samples()
    {
        var sampleId = Guid.NewGuid();
        var replay = VoiceChoonReplay.Create(
            "abcdefghijklmnopqrstuvwx",
            Guid.NewGuid(),
            Guid.NewGuid(),
            "host-user",
            "Greensleeves",
            "{\"phase\":\"Results\"}",
            [sampleId, sampleId],
            DateTimeOffset.UtcNow);

        Assert.Equal("abcdefghijklmnopqrstuvwx", replay.ShareCode);
        Assert.Equal([sampleId], replay.SampleAssetIds);
    }

    [Fact]
    public void Replay_rejects_an_unbounded_snapshot()
    {
        Assert.Throws<ArgumentException>(() => VoiceChoonReplay.Create(
            "abcdefghijklmnopqrstuvwx",
            Guid.NewGuid(),
            Guid.NewGuid(),
            "host-user",
            "Song",
            new string('x', VoiceChoonReplay.MaximumSnapshotCharacters + 1),
            [Guid.NewGuid()],
            DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Retained_voice_sample_no_longer_expires()
    {
        var sample = VoiceSampleMetadata.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "lead",
            "ab/abcdef.webm", "audio/webm", 1024, DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow.AddDays(1));

        sample.RetainForReplay();

        Assert.True(sample.IsRetainedForReplay);
        Assert.Equal(DateTimeOffset.MaxValue, sample.ExpiresAtUtc);
    }
}
