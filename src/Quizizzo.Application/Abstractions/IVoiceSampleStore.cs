namespace Quizizzo.Application.Abstractions;

public interface IVoiceSampleStore
{
    Task<VoiceSampleReference> SaveAsync(
        VoiceSampleUpload sample,
        CancellationToken cancellationToken = default);

    Task<VoiceSampleContent?> GetAsync(
        string key,
        CancellationToken cancellationToken = default);

    Task<int> DeleteExpiredAsync(
        DateTimeOffset expiresBeforeUtc,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(string key, CancellationToken cancellationToken = default);
}

public sealed record VoiceSampleUpload(ReadOnlyMemory<byte> Content, string ContentType);

public sealed record VoiceSampleReference(
    string Key,
    string ContentType,
    long Length,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset ExpiresAtUtc);

public sealed record VoiceSampleContent(ReadOnlyMemory<byte> Content, string ContentType);
