using Quizizzo.Domain.Parties;

namespace Quizizzo.Application.Abstractions;

public interface IPartyRepository
{
    Task<bool> ActiveRoomCodeExistsAsync(RoomCode roomCode, CancellationToken cancellationToken);
    Task AddAsync(Party party, CancellationToken cancellationToken);
    Task<Party?> GetByIdAsync(PartyId partyId, CancellationToken cancellationToken);
    Task<Party?> GetByRoomCodeAsync(RoomCode roomCode, CancellationToken cancellationToken);
    Task<Party?> GetActiveByHostAsync(string hostUserId, CancellationToken cancellationToken);
    Task<IReadOnlyList<Party>> ListRecentByHostAsync(string hostUserId, int limit, CancellationToken cancellationToken);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
