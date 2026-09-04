using Microsoft.Extensions.Options;
using Quizizzo.Application.Abstractions;
using Quizizzo.Infrastructure.Voice;

namespace Quizizzo.IntegrationTests;

public sealed class VoiceSampleStoreTests : IDisposable
{
    private readonly DateTimeOffset now = new(2026, 9, 4, 12, 0, 0, TimeSpan.Zero);
    private readonly string rootPath = Path.Combine(
        Path.GetTempPath(), "quizizzo-voice-store-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Filesystem_store_round_trips_bounded_wave_and_rejects_untrusted_content()
    {
        var store = CreateStore();
        byte[] wave =
        [
            0x52, 0x49, 0x46, 0x46, 0x04, 0, 0, 0,
            0x57, 0x41, 0x56, 0x45
        ];

        var reference = await store.SaveAsync(new VoiceSampleUpload(wave, "audio/wav"));
        var restored = await store.GetAsync(reference.Key);

        Assert.Matches("^[0-9a-f]{2}/[0-9a-f]{32}\\.wav$", reference.Key);
        Assert.Equal(now.AddDays(1), reference.ExpiresAtUtc);
        Assert.Equal(wave, restored!.Content.ToArray());
        await Assert.ThrowsAsync<ArgumentException>(() => store.GetAsync("../secret.wav"));
        await Assert.ThrowsAsync<ArgumentException>(() => store.SaveAsync(
            new VoiceSampleUpload("not audio"u8.ToArray(), "audio/wav")));
    }

    private FileSystemVoiceSampleStore CreateStore() => new(
        Options.Create(new VoiceSampleStoreOptions
        {
            RootPath = rootPath,
            MaximumAssetBytes = 2048,
            RetentionPeriod = TimeSpan.FromDays(1)
        }),
        new FixedTimeProvider(now));

    public void Dispose()
    {
        if (Directory.Exists(rootPath))
        {
            Directory.Delete(rootPath, recursive: true);
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
