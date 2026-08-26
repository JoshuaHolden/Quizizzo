using Quizizzo.Domain.Players;

namespace Quizizzo.Application.Players;

public sealed record CharacterView(
    CharacterBodyType BodyType,
    string PrimaryColour,
    CharacterEyes Eyes,
    CharacterMouth Mouth,
    CharacterAccessory Accessory);

public sealed record PlayerView(
    Guid PlayerId,
    Guid PartyId,
    string RoomCode,
    string DisplayName,
    int Score,
    PlayerStatus Status,
    CharacterView Character,
    DateTimeOffset JoinedAt,
    DateTimeOffset LastSeenAt);

public sealed record JoinedPlayer(string SessionToken, bool IsNew, PlayerView View);

public sealed record JoinPartyView(Guid PartyId, string RoomCode, int PlayerCount, int MaximumPlayers);
