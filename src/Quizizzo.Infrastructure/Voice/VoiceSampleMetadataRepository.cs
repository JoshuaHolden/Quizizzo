using Microsoft.EntityFrameworkCore;
using Npgsql;
using Quizizzo.Application.Abstractions;
using Quizizzo.Domain.Voice;
using Quizizzo.Infrastructure.Identity;

namespace Quizizzo.Infrastructure.Voice;

public sealed class VoiceSampleMetadataRepository(ApplicationDbContext dbContext)
    : IVoiceSampleMetadataRepository
{
    public Task<VoiceSampleMetadata?> GetByIdAsync(Guid assetId, CancellationToken cancellationToken = default) =>
        dbContext.VoiceSamples.AsNoTracking().SingleOrDefaultAsync(sample => sample.Id == assetId, cancellationToken);

    public Task<VoiceSampleMetadata?> FindSubmissionAsync(
        Guid submissionId,
        Guid gameInstanceId,
        Guid playerId,
        string promptKey,
        CancellationToken cancellationToken = default) =>
        dbContext.VoiceSamples.AsNoTracking().SingleOrDefaultAsync(sample =>
            sample.SubmissionId == submissionId && sample.GameInstanceId == gameInstanceId &&
            sample.PlayerId == playerId && sample.PromptKey == promptKey, cancellationToken);

    public async Task<bool> TryAddAsync(
        VoiceSampleMetadata asset,
        CancellationToken cancellationToken = default)
    {
        await dbContext.VoiceSamples.AddAsync(asset, cancellationToken);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException exception) when (
            exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            dbContext.ChangeTracker.Clear();
            return false;
        }
    }

    public async Task<IReadOnlyList<VoiceSampleMetadata>> RetainForReplayAsync(
        IReadOnlyCollection<Guid> assetIds,
        Guid gameInstanceId,
        CancellationToken cancellationToken = default)
    {
        var samples = await dbContext.VoiceSamples
            .Where(sample => assetIds.Contains(sample.Id) && sample.GameInstanceId == gameInstanceId)
            .ToListAsync(cancellationToken);
        if (samples.Count != assetIds.Distinct().Count())
        {
            throw new InvalidOperationException("One or more replay voice samples are unavailable.");
        }
        foreach (var sample in samples)
        {
            sample.RetainForReplay();
        }
        await dbContext.SaveChangesAsync(cancellationToken);
        return samples;
    }

    public Task<int> DeleteExpiredAsync(
        DateTimeOffset expiresAtOrBeforeUtc,
        CancellationToken cancellationToken = default) =>
        dbContext.VoiceSamples
            .Where(sample => !sample.IsRetainedForReplay && sample.ExpiresAtUtc <= expiresAtOrBeforeUtc)
            .ExecuteDeleteAsync(cancellationToken);

    public Task DeleteAsync(Guid assetId, CancellationToken cancellationToken = default) =>
        dbContext.VoiceSamples.Where(sample => sample.Id == assetId).ExecuteDeleteAsync(cancellationToken);
}
