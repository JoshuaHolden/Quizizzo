using Quizizzo.Application.Abstractions;
using Quizizzo.Application.Parties;
using Quizizzo.Domain;
using Quizizzo.Domain.Parties;
using Quizizzo.Domain.Players;

namespace Quizizzo.Application.Players;

public sealed class PlayerService(
    IPartyRepository parties,
    IPlayerRepository players,
    IPlayerCredentialService credentials,
    ICharacterGenerator characters,
    PartyMutationCoordinator partyMutations,
    TimeProvider timeProvider)
{
    public async Task<JoinPartyView> GetJoinPartyAsync(
        string roomCode,
        CancellationToken cancellationToken = default)
    {
        var party = await GetJoinablePartyAsync(roomCode, cancellationToken);
        var count = await players.CountMembersAsync(party.Id, cancellationToken);
        return new JoinPartyView(party.Id.Value, party.RoomCode.Value, count, QuizizzoLimits.MaximumPlayers);
    }

    public async Task<JoinedPlayer> JoinAsync(
        string roomCode,
        string displayName,
        string? existingSessionToken = null,
        CancellationToken cancellationToken = default)
    {
        var discoveredParty = await GetJoinablePartyAsync(roomCode, cancellationToken);
        await using var mutation = await partyMutations.AcquireAsync(
            discoveredParty.Id, cancellationToken);
        var party = await parties.GetByIdAsync(discoveredParty.Id, cancellationToken)
            ?? throw new PartyNotFoundException();
        EnsurePartyAcceptsPlayers(party);

        if (!string.IsNullOrWhiteSpace(existingSessionToken))
        {
            var existing = await players.GetBySessionTokenHashAsync(
                credentials.HashSessionToken(existingSessionToken), cancellationToken);
            if (existing is not null && existing.PartyId == party.Id)
            {
                existing.Reconnect(timeProvider.GetUtcNow());
                await players.SaveChangesAsync(cancellationToken);
                return new JoinedPlayer(
                    existingSessionToken,
                    false,
                    Map(existing, party.RoomCode));
            }
        }

        if (await players.CountMembersAsync(party.Id, cancellationToken) >= QuizizzoLimits.MaximumPlayers)
        {
            throw new InvalidOperationException("This party is full.");
        }

        var playerName = PlayerName.Parse(displayName);
        var sessionToken = credentials.GenerateSessionToken();
        var player = Player.Create(
            party.Id,
            playerName,
            characters.Generate(),
            credentials.HashSessionToken(sessionToken),
            timeProvider.GetUtcNow());
        await players.AddAsync(player, cancellationToken);
        await players.SaveChangesAsync(cancellationToken);
        return new JoinedPlayer(sessionToken, true, Map(player, party.RoomCode));
    }

    public async Task<PlayerView> ReconnectAsync(
        string sessionToken,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionToken);
        var player = await players.GetBySessionTokenHashAsync(
            credentials.HashSessionToken(sessionToken), cancellationToken)
            ?? throw new PlayerSessionNotFoundException();
        var party = await parties.GetByIdAsync(player.PartyId, cancellationToken)
            ?? throw new PartyNotFoundException();
        if (!party.HasActiveRoomCode)
        {
            throw new InvalidOperationException("This party has ended.");
        }

        player.Reconnect(timeProvider.GetUtcNow());
        await players.SaveChangesAsync(cancellationToken);
        return Map(player, party.RoomCode);
    }

    public async Task<PlayerView> GetByIdAsync(
        Guid playerId,
        CancellationToken cancellationToken = default)
    {
        var player = await players.GetByIdAsync(new PlayerId(playerId), cancellationToken)
            ?? throw new PlayerSessionNotFoundException();
        var party = await parties.GetByIdAsync(player.PartyId, cancellationToken)
            ?? throw new PartyNotFoundException();
        return Map(player, party.RoomCode);
    }

    public async Task<PlayerView> MarkDisconnectedAsync(
        Guid playerId,
        CancellationToken cancellationToken = default)
    {
        var player = await players.GetByIdAsync(new PlayerId(playerId), cancellationToken)
            ?? throw new PlayerSessionNotFoundException();
        var party = await parties.GetByIdAsync(player.PartyId, cancellationToken)
            ?? throw new PartyNotFoundException();
        player.MarkDisconnected(timeProvider.GetUtcNow());
        await players.SaveChangesAsync(cancellationToken);
        return Map(player, party.RoomCode);
    }

    public async Task<IReadOnlyList<PlayerView>> ListForHostAsync(
        Guid partyId,
        string hostUserId,
        CancellationToken cancellationToken = default)
    {
        var party = await parties.GetByIdAsync(new PartyId(partyId), cancellationToken)
            ?? throw new PartyNotFoundException();
        if (!party.IsOwnedBy(hostUserId))
        {
            throw new PartyAccessDeniedException();
        }

        return await ListAsync(party, cancellationToken);
    }

    public async Task<IReadOnlyList<PlayerView>> ListForDisplayAsync(
        Guid partyId,
        CancellationToken cancellationToken = default)
    {
        var party = await parties.GetByIdAsync(new PartyId(partyId), cancellationToken)
            ?? throw new PartyNotFoundException();
        return await ListAsync(party, cancellationToken);
    }

    private async Task<Party> GetJoinablePartyAsync(string value, CancellationToken cancellationToken)
    {
        if (!RoomCode.TryCreate(value, out var roomCode))
        {
            throw new ArgumentException("Enter a valid four-character room code.", nameof(value));
        }

        var party = await parties.GetByRoomCodeAsync(roomCode, cancellationToken)
            ?? throw new PartyNotFoundException();
        EnsurePartyAcceptsPlayers(party);

        return party;
    }

    private static void EnsurePartyAcceptsPlayers(Party party)
    {
        if (party.Status != PartyStatus.Lobby)
        {
            throw new InvalidOperationException("This party is not accepting players right now.");
        }
    }

    private async Task<IReadOnlyList<PlayerView>> ListAsync(Party party, CancellationToken cancellationToken)
    {
        var members = await players.ListMembersAsync(party.Id, cancellationToken);
        return members.Select(player => Map(player, party.RoomCode)).ToArray();
    }

    private static PlayerView Map(Player player, RoomCode roomCode) => new(
        player.Id.Value,
        player.PartyId.Value,
        roomCode.Value,
        player.DisplayName.Value,
        player.Score,
        player.Status,
        new CharacterView(
            player.Character.BodyType,
            player.Character.PrimaryColour,
            player.Character.Eyes,
            player.Character.Mouth,
            player.Character.Accessory),
        player.JoinedAt,
        player.LastSeenAt);
}
