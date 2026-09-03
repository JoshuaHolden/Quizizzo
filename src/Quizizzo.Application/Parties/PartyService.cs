using Quizizzo.Application.Abstractions;
using Quizizzo.Domain.Parties;

namespace Quizizzo.Application.Parties;

public sealed class PartyService(
    IPartyRepository parties,
    IRoomCodeGenerator roomCodes,
    PartyMutationCoordinator partyMutations,
    TimeProvider timeProvider)
{
    private const int RoomCodeAttempts = 32;

    public async Task<PartyView> CreateAsync(string hostUserId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hostUserId);

        if (await parties.GetActiveByHostAsync(hostUserId, cancellationToken) is not null)
        {
            throw new InvalidOperationException("This host already has an active party.");
        }

        for (var attempt = 0; attempt < RoomCodeAttempts; attempt++)
        {
            var roomCode = roomCodes.Generate();
            if (await parties.ActiveRoomCodeExistsAsync(roomCode, cancellationToken))
            {
                continue;
            }

            var party = Party.Create(hostUserId, roomCode, timeProvider.GetUtcNow());
            await parties.AddAsync(party, cancellationToken);
            await parties.SaveChangesAsync(cancellationToken);
            return Map(party);
        }

        throw new InvalidOperationException("A unique active room code could not be allocated.");
    }

    public async Task<PartyView> GetOwnedAsync(
        Guid partyId,
        string hostUserId,
        CancellationToken cancellationToken = default)
    {
        var party = await GetOwnedPartyAsync(partyId, hostUserId, cancellationToken);
        return Map(party);
    }

    public async Task<PartyView?> GetActiveAsync(string hostUserId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hostUserId);
        var party = await parties.GetActiveByHostAsync(hostUserId, cancellationToken);
        return party is null ? null : Map(party);
    }

    public async Task<PartyView> CloseLobbyAsync(
        Guid partyId,
        string hostUserId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hostUserId);
        await using var mutation = await partyMutations.AcquireAsync(
            new PartyId(partyId), cancellationToken);
        var party = await GetOwnedPartyAsync(partyId, hostUserId, cancellationToken);
        party.Abandon(timeProvider.GetUtcNow());
        await parties.SaveChangesAsync(cancellationToken);
        return Map(party);
    }

    public async Task<IReadOnlyList<PartyView>> ListRecentAsync(
        string hostUserId,
        int limit = 10,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hostUserId);
        if (limit is < 1 or > 50)
        {
            throw new ArgumentOutOfRangeException(nameof(limit));
        }

        var recent = await parties.ListRecentByHostAsync(hostUserId, limit, cancellationToken);
        return recent.Select(Map).ToArray();
    }

    internal async Task<Party> GetOwnedPartyAsync(
        Guid partyId,
        string hostUserId,
        CancellationToken cancellationToken)
    {
        var party = await parties.GetByIdAsync(new PartyId(partyId), cancellationToken)
            ?? throw new PartyNotFoundException();
        if (!party.IsOwnedBy(hostUserId))
        {
            throw new PartyAccessDeniedException();
        }

        return party;
    }

    private static PartyView Map(Party party) => new(
        party.Id.Value,
        party.RoomCode.Value,
        party.Status,
        party.CreatedAt,
        party.CompletedAt,
        party.CurrentGameInstanceId,
        party.CurrentGameKey,
        party.GameQueue.ToArray());
}
