namespace Quizizzo.Application.Abstractions;

public interface IDrawingAssetStore
{
    Task<DrawingAssetReference> SaveAsync(
        DrawingAssetUpload asset,
        CancellationToken cancellationToken = default);

    Task<DrawingAssetContent?> GetAsync(
        string key,
        CancellationToken cancellationToken = default);

    Task<int> DeleteExpiredAsync(
        DateTimeOffset expiresBeforeUtc,
        CancellationToken cancellationToken = default);
}

public sealed record DrawingAssetUpload(
    ReadOnlyMemory<byte> Content,
    string ContentType);

public sealed record DrawingAssetReference(
    string Key,
    string ContentType,
    long Length,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset ExpiresAtUtc);

public sealed record DrawingAssetContent(
    ReadOnlyMemory<byte> Content,
    string ContentType);
