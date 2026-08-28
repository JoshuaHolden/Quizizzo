using Microsoft.EntityFrameworkCore;
using Quizizzo.Application.Abstractions;
using Quizizzo.Domain.Parties;
using Quizizzo.Infrastructure.Identity;

namespace Quizizzo.Infrastructure.Parties;

public sealed class PartyRepository(ApplicationDbContext dbContext) : IPartyRepository
{
    public Task<bool> ActiveRoomCodeExistsAsync(RoomCode roomCode, CancellationToken cancellationToken) =>
        dbContext.Parties.AnyAsync(
            party => party.RoomCode == roomCode &&
                (party.Status == PartyStatus.Created ||
                 party.Status == PartyStatus.Lobby ||
                 party.Status == PartyStatus.Playing ||
                 party.Status == PartyStatus.Paused),
            cancellationToken);

    public async Task AddAsync(Party party, CancellationToken cancellationToken) =>
        await dbContext.Parties.AddAsync(party, cancellationToken);

    public Task<Party?> GetByIdAsync(PartyId partyId, CancellationToken cancellationToken) =>
        dbContext.Parties.SingleOrDefaultAsync(party => party.Id == partyId, cancellationToken);

    public Task<Party?> GetByRoomCodeAsync(RoomCode roomCode, CancellationToken cancellationToken) =>
        dbContext.Parties.AsNoTracking().SingleOrDefaultAsync(
            party => party.RoomCode == roomCode &&
                (party.Status == PartyStatus.Created ||
                 party.Status == PartyStatus.Lobby ||
                 party.Status == PartyStatus.Playing ||
                 party.Status == PartyStatus.Paused),
            cancellationToken);

    public Task<Party?> GetActiveByHostAsync(string hostUserId, CancellationToken cancellationToken) =>
        dbContext.Parties
            .Where(party => party.HostUserId == hostUserId &&
                (party.Status == PartyStatus.Created ||
                 party.Status == PartyStatus.Lobby ||
                 party.Status == PartyStatus.Playing ||
                 party.Status == PartyStatus.Paused))
            .OrderByDescending(party => party.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<Party>> ListRecentByHostAsync(
        string hostUserId,
        int limit,
        CancellationToken cancellationToken) =>
        await dbContext.Parties
            .Where(party => party.HostUserId == hostUserId)
            .OrderByDescending(party => party.CreatedAt)
            .Take(limit)
            .ToListAsync(cancellationToken);

    public async Task SaveChangesAsync(CancellationToken cancellationToken) =>
        await dbContext.SaveChangesAsync(cancellationToken);
}
