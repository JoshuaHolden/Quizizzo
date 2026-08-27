using Microsoft.Extensions.Options;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Quizizzo.Application.Abstractions;
using Quizizzo.Infrastructure.Drawings;
using Quizizzo.Infrastructure.Health;

namespace Quizizzo.IntegrationTests;

public sealed class DrawingAssetStoreTests : IDisposable
{
    private readonly AdjustableTimeProvider clock = new(
        new DateTimeOffset(2026, 8, 27, 12, 0, 0, TimeSpan.Zero));
    private readonly string rootPath = Path.Combine(
        Path.GetTempPath(),
        "quizizzo-drawing-store-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Filesystem_store_round_trips_an_opaque_drawing_asset()
    {
        var store = CreateStore();
        byte[] bytes = [0x52, 0x49, 0x46, 0x46, 0x04, 0x00, 0x00, 0x00, 0x57, 0x45, 0x42, 0x50];

        var reference = await store.SaveAsync(new DrawingAssetUpload(bytes, "image/webp"));
        var restored = await store.GetAsync(reference.Key);

        Assert.Matches("^[0-9a-f]{2}/[0-9a-f]{32}\\.webp$", reference.Key);
        Assert.Equal("image/webp", reference.ContentType);
        Assert.Equal(bytes.Length, reference.Length);
        Assert.Equal(clock.GetUtcNow(), reference.CreatedAtUtc);
        Assert.Equal(clock.GetUtcNow().AddDays(1), reference.ExpiresAtUtc);
        Assert.NotNull(restored);
        Assert.Equal(bytes, restored.Content.ToArray());
        Assert.Equal("image/webp", restored.ContentType);
    }

    [Fact]
    public async Task Filesystem_store_rejects_unsupported_or_oversized_content()
    {
        var store = CreateStore(maximumAssetBytes: 1024);

        await Assert.ThrowsAsync<ArgumentException>(() => store.SaveAsync(
            new DrawingAssetUpload(new byte[1], "image/svg+xml")));
        await Assert.ThrowsAsync<ArgumentException>(() => store.SaveAsync(
            new DrawingAssetUpload(new byte[1025], "image/png")));
        await Assert.ThrowsAsync<ArgumentException>(() => store.SaveAsync(
            new DrawingAssetUpload(new byte[12], "image/webp")));
    }

    [Fact]
    public async Task Cancelled_write_does_not_commit_or_leave_a_temporary_asset()
    {
        var store = CreateStore();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        byte[] bytes = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.SaveAsync(
            new DrawingAssetUpload(bytes, "image/png"), cancellation.Token));

        Assert.False(Directory.Exists(rootPath));
    }

    [Fact]
    public async Task Oversized_existing_asset_is_rejected_before_it_is_allocated()
    {
        var store = CreateStore(maximumAssetBytes: 1024);
        byte[] webp = [0x52, 0x49, 0x46, 0x46, 0x04, 0x00, 0x00, 0x00, 0x57, 0x45, 0x42, 0x50];
        var reference = await store.SaveAsync(new DrawingAssetUpload(webp, "image/webp"));
        var path = Path.Combine(rootPath, reference.Key.Replace('/', Path.DirectorySeparatorChar));
        await File.WriteAllBytesAsync(path, new byte[1025]);

        await Assert.ThrowsAsync<InvalidDataException>(() => store.GetAsync(reference.Key));
    }

    [Fact]
    public async Task Drawing_asset_health_check_proves_the_root_is_writeable()
    {
        var options = Options.Create(new DrawingAssetStoreOptions
        {
            RootPath = rootPath,
            MaximumAssetBytes = 2048
        });
        var healthCheck = new DrawingAssetStoreHealthCheck(options);

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Empty(Directory.EnumerateFiles(rootPath));
    }

    [Fact]
    public async Task Expiry_sweep_deletes_only_assets_older_than_the_one_day_ttl()
    {
        var store = CreateStore();
        byte[] webp = [0x52, 0x49, 0x46, 0x46, 0x04, 0x00, 0x00, 0x00, 0x57, 0x45, 0x42, 0x50];
        var expired = await store.SaveAsync(new DrawingAssetUpload(webp, "image/webp"));
        clock.Advance(TimeSpan.FromDays(1) + TimeSpan.FromMinutes(1));
        var current = await store.SaveAsync(new DrawingAssetUpload(webp, "image/webp"));

        var deleted = await store.DeleteExpiredAsync(clock.GetUtcNow().Subtract(TimeSpan.FromDays(1)));

        Assert.Equal(1, deleted);
        Assert.Null(await store.GetAsync(expired.Key));
        Assert.NotNull(await store.GetAsync(current.Key));
    }

    [Fact]
    public async Task Filesystem_store_rejects_untrusted_keys_and_returns_null_for_missing_assets()
    {
        var store = CreateStore();

        await Assert.ThrowsAsync<ArgumentException>(() => store.GetAsync("../secret.webp"));
        Assert.Null(await store.GetAsync($"ab/{Guid.NewGuid():N}.webp"));
    }

    private FileSystemDrawingAssetStore CreateStore(int maximumAssetBytes = 2048) => new(
        Options.Create(new DrawingAssetStoreOptions
        {
            RootPath = rootPath,
            MaximumAssetBytes = maximumAssetBytes,
            RetentionPeriod = TimeSpan.FromDays(1)
        }),
        clock);

    public void Dispose()
    {
        if (Directory.Exists(rootPath))
        {
            Directory.Delete(rootPath, recursive: true);
        }
    }

    private sealed class AdjustableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;

        public void Advance(TimeSpan amount) => now = now.Add(amount);
    }
}
