using Quizizzo.Domain.Players;

namespace Quizizzo.Application.Players;

public sealed record CharacterView(
    CharacterBodyType BodyType,
    string PrimaryColour,
    CharacterEyes Eyes,
    CharacterMouth Mouth,
    CharacterAccessory Accessory,
    CharacterPresentation Presentation = CharacterPresentation.Man,
    CharacterSkinTone SkinTone = CharacterSkinTone.Tint1,
    CharacterHairColour HairColour = CharacterHairColour.Brown,
    CharacterShirtColour ShirtColour = CharacterShirtColour.Navy,
    CharacterTrouserColour TrouserColour = CharacterTrouserColour.Navy,
    CharacterTrouserLength TrouserLength = CharacterTrouserLength.FullLength,
    CharacterShoeColour ShoeColour = CharacterShoeColour.Brown);

public sealed record CharacterSelection(
    CharacterPresentation Presentation,
    CharacterSkinTone SkinTone,
    CharacterHairColour HairColour,
    CharacterShirtColour ShirtColour,
    CharacterTrouserColour TrouserColour,
    CharacterTrouserLength TrouserLength,
    CharacterShoeColour ShoeColour)
{
    public CharacterDefinition ToDefinition() => new(
        Presentation, SkinTone, HairColour, ShirtColour,
        TrouserColour, TrouserLength, ShoeColour);
}

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
