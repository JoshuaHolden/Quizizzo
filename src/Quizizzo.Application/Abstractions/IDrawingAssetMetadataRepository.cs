using Quizizzo.Domain.Drawings;

namespace Quizizzo.Application.Abstractions;

public interface IDrawingAssetMetadataRepository
{
    Task<DrawingAssetMetadata?> GetByIdAsync(Guid assetId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DrawingAssetMetadata>> ListSubmissionAsync(
        Guid submissionId,
        Guid gameInstanceId,
        Guid playerId,
        string roundId,
        CancellationToken cancellationToken = default);

    Task<bool> TryAddSubmissionAsync(
        IReadOnlyCollection<DrawingAssetMetadata> assets,
        CancellationToken cancellationToken = default);

    Task<int> DeleteExpiredAsync(
        DateTimeOffset expiresAtOrBeforeUtc,
        CancellationToken cancellationToken = default);
}
