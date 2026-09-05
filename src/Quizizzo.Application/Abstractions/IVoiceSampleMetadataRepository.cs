using Quizizzo.Domain.Voice;

namespace Quizizzo.Application.Abstractions;

public interface IVoiceSampleMetadataRepository
{
    Task<VoiceSampleMetadata?> GetByIdAsync(Guid assetId, CancellationToken cancellationToken = default);

    Task<VoiceSampleMetadata?> FindSubmissionAsync(
        Guid submissionId,
        Guid gameInstanceId,
        Guid playerId,
        string promptKey,
        CancellationToken cancellationToken = default);

    Task<bool> TryAddAsync(VoiceSampleMetadata asset, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<VoiceSampleMetadata>> RetainForReplayAsync(
        IReadOnlyCollection<Guid> assetIds,
        Guid gameInstanceId,
        CancellationToken cancellationToken = default);

    Task<int> DeleteExpiredAsync(
        DateTimeOffset expiresAtOrBeforeUtc,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(Guid assetId, CancellationToken cancellationToken = default);
}
