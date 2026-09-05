using Microsoft.EntityFrameworkCore;
using Quizizzo.Application.Abstractions;
using Quizizzo.Domain.Voice;
using Quizizzo.Infrastructure.Identity;

namespace Quizizzo.Infrastructure.Voice;

public sealed class VoiceChoonReplayRepository(ApplicationDbContext dbContext) : IVoiceChoonReplayRepository
{
    public Task<VoiceChoonReplay?> GetByGameInstanceAsync(
        Guid gameInstanceId,
        CancellationToken cancellationToken = default) =>
        dbContext.VoiceChoonReplays.AsNoTracking()
            .SingleOrDefaultAsync(replay => replay.GameInstanceId == gameInstanceId, cancellationToken);

    public Task<VoiceChoonReplay?> GetByShareCodeAsync(
        string shareCode,
        CancellationToken cancellationToken = default) =>
        dbContext.VoiceChoonReplays.AsNoTracking()
            .SingleOrDefaultAsync(replay => replay.ShareCode == shareCode, cancellationToken);

    public async Task AddAsync(VoiceChoonReplay replay, CancellationToken cancellationToken = default)
    {
        await dbContext.VoiceChoonReplays.AddAsync(replay, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(VoiceChoonReplay replay, CancellationToken cancellationToken = default)
    {
        dbContext.VoiceChoonReplays.Remove(replay);
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
