using Microsoft.EntityFrameworkCore;
using Npgsql;
using Quizizzo.Application.Abstractions;
using Quizizzo.Domain.Drawings;
using Quizizzo.Infrastructure.Identity;

namespace Quizizzo.Infrastructure.Drawings;

public sealed class DrawingAssetMetadataRepository(ApplicationDbContext dbContext)
    : IDrawingAssetMetadataRepository
{
    public Task<DrawingAssetMetadata?> GetByIdAsync(
        Guid assetId,
        CancellationToken cancellationToken = default) =>
        dbContext.DrawingAssets.AsNoTracking().SingleOrDefaultAsync(
            asset => asset.Id == assetId, cancellationToken);

    public async Task<IReadOnlyList<DrawingAssetMetadata>> ListSubmissionAsync(
        Guid submissionId,
        Guid gameInstanceId,
        Guid playerId,
        string roundId,
        CancellationToken cancellationToken = default) =>
        await dbContext.DrawingAssets.AsNoTracking()
            .Where(asset => asset.SubmissionId == submissionId &&
                asset.GameInstanceId == gameInstanceId &&
                asset.PlayerId == playerId &&
                asset.RoundId == roundId)
            .OrderBy(asset => asset.FrameNumber)
            .ToListAsync(cancellationToken);

    public async Task<bool> TryAddSubmissionAsync(
        IReadOnlyCollection<DrawingAssetMetadata> assets,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(assets);
        if (assets.Count == 0)
        {
            throw new ArgumentException("At least one drawing asset is required.", nameof(assets));
        }

        await dbContext.DrawingAssets.AddRangeAsync(assets, cancellationToken);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException exception) when (
            exception.InnerException is PostgresException
            {
                SqlState: PostgresErrorCodes.UniqueViolation
            })
        {
            // A concurrent retry registered this submission first. Clear the failed
            // additions so subsequent no-tracking reads use only committed rows.
            dbContext.ChangeTracker.Clear();
            return false;
        }
    }

    public Task<int> DeleteExpiredAsync(
        DateTimeOffset expiresAtOrBeforeUtc,
        CancellationToken cancellationToken = default) =>
        dbContext.DrawingAssets
            .Where(asset => asset.ExpiresAtUtc <= expiresAtOrBeforeUtc)
            .ExecuteDeleteAsync(cancellationToken);
}
