using Quizizzo.Domain.Parties;
using Quizizzo.Domain.Players;

namespace Quizizzo.Application.Abstractions;

public interface IPlayerRepository
{
    Task<int> CountMembersAsync(PartyId partyId, CancellationToken cancellationToken);
    Task AddAsync(Player player, CancellationToken cancellationToken);
    Task<Player?> GetByIdAsync(PlayerId playerId, CancellationToken cancellationToken);
    Task<Player?> GetBySessionTokenHashAsync(string tokenHash, CancellationToken cancellationToken);
    Task<IReadOnlyList<Player>> ListMembersAsync(PartyId partyId, CancellationToken cancellationToken);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
