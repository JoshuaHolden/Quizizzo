using Microsoft.EntityFrameworkCore;
using Quizizzo.Application.Abstractions;
using Quizizzo.Domain.Parties;
using Quizizzo.Domain.Players;
using Quizizzo.Infrastructure.Identity;

namespace Quizizzo.Infrastructure.Players;

public sealed class PlayerRepository(ApplicationDbContext dbContext) : IPlayerRepository
{
    public Task<int> CountMembersAsync(PartyId partyId, CancellationToken cancellationToken) =>
        dbContext.Players.CountAsync(
            player => player.PartyId == partyId &&
                (player.Status == PlayerStatus.Connected || player.Status == PlayerStatus.Disconnected),
            cancellationToken);

    public async Task AddAsync(Player player, CancellationToken cancellationToken) =>
        await dbContext.Players.AddAsync(player, cancellationToken);

    public Task<Player?> GetByIdAsync(PlayerId playerId, CancellationToken cancellationToken) =>
        dbContext.Players.SingleOrDefaultAsync(player => player.Id == playerId, cancellationToken);

    public Task<Player?> GetBySessionTokenHashAsync(string tokenHash, CancellationToken cancellationToken) =>
        dbContext.Players.SingleOrDefaultAsync(
            player => player.SessionTokenHash == tokenHash, cancellationToken);

    public async Task<IReadOnlyList<Player>> ListMembersAsync(
        PartyId partyId,
        CancellationToken cancellationToken) =>
        await dbContext.Players
            .Where(player => player.PartyId == partyId &&
                (player.Status == PlayerStatus.Connected || player.Status == PlayerStatus.Disconnected))
            .OrderBy(player => player.JoinedAt)
            .ToListAsync(cancellationToken);

    public async Task SaveChangesAsync(CancellationToken cancellationToken) =>
        await dbContext.SaveChangesAsync(cancellationToken);
}
