using Quizizzo.Domain.Players;
using Quizizzo.Domain.Parties;

namespace Quizizzo.Domain.Tests;

public sealed class PlayerTests
{
    private static readonly CharacterDefinition Character = new(
        CharacterBodyType.Bean,
        "#FF4D6D",
        CharacterEyes.Bright,
        CharacterMouth.Smile,
        CharacterAccessory.None);

    [Fact]
    public void Player_name_is_trimmed_and_limited()
    {
        Assert.Equal("Joshua", PlayerName.Parse("  Joshua  ").Value);
        Assert.Throws<ArgumentException>(() => PlayerName.Parse(new string('x', 25)));
        Assert.Throws<ArgumentException>(() => PlayerName.Parse("na\rme"));
    }

    [Fact]
    public void Persisted_party_score_can_be_updated_by_game_orchestration()
    {
        var player = Player.Create(
            PartyId.New(),
            PlayerName.Parse("Joshua"),
            Character,
            "HASH",
            DateTimeOffset.UtcNow);

        player.SetScore(1600);

        Assert.Equal(1600, player.Score);
        Assert.Throws<ArgumentOutOfRangeException>(() => player.SetScore(-1));
    }

    [Fact]
    public void Player_can_reconnect_with_the_same_identity_and_character()
    {
        var now = new DateTimeOffset(2026, 8, 26, 12, 0, 0, TimeSpan.Zero);
        var player = Player.Create(PartyId.New(), PlayerName.Parse("Joshua"), Character, "HASH", now);
        var playerId = player.Id;

        player.MarkDisconnected(now.AddMinutes(1));
        player.Reconnect(now.AddMinutes(2));

        Assert.Equal(playerId, player.Id);
        Assert.Same(Character, player.Character);
        Assert.Equal(PlayerStatus.Connected, player.Status);
        Assert.Equal(now.AddMinutes(2), player.LastSeenAt);
    }

    [Fact]
    public void Kicked_player_is_no_longer_a_party_member_or_allowed_to_reconnect()
    {
        var now = new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);
        var player = Player.Create(PartyId.New(), PlayerName.Parse("Joshua"), Character, "HASH", now);

        player.Kick(now.AddMinutes(1));

        Assert.Equal(PlayerStatus.Kicked, player.Status);
        Assert.False(player.IsPartyMember);
        Assert.Throws<InvalidOperationException>(() => player.Reconnect(now.AddMinutes(2)));
    }

    [Theory]
    [InlineData(CharacterShirtStyle.Style4)]
    [InlineData(CharacterShirtStyle.Style8)]
    public void Woman_presentation_accepts_only_woman_top_styles(CharacterShirtStyle shirtStyle)
    {
        var character = CreateDesignedCharacter(CharacterPresentation.Woman, shirtStyle);

        Assert.Equal(shirtStyle, character.ShirtStyle);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CreateDesignedCharacter(CharacterPresentation.Man, shirtStyle));
    }

    [Theory]
    [InlineData(CharacterShirtStyle.Style1)]
    [InlineData(CharacterShirtStyle.Style2)]
    [InlineData(CharacterShirtStyle.Style3)]
    [InlineData(CharacterShirtStyle.Style5)]
    [InlineData(CharacterShirtStyle.Style6)]
    [InlineData(CharacterShirtStyle.Style7)]
    public void Man_presentation_accepts_only_man_top_styles(CharacterShirtStyle shirtStyle)
    {
        var character = CreateDesignedCharacter(CharacterPresentation.Man, shirtStyle);

        Assert.Equal(shirtStyle, character.ShirtStyle);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CreateDesignedCharacter(CharacterPresentation.Woman, shirtStyle));
    }

    [Theory]
    [InlineData(CharacterBodySize.Thin)]
    [InlineData(CharacterBodySize.Normal)]
    [InlineData(CharacterBodySize.Thick)]
    public void Designed_characters_preserve_the_selected_body_size(CharacterBodySize bodySize)
    {
        var character = new CharacterDefinition(
            CharacterPresentation.Man,
            CharacterSkinTone.Tint1,
            CharacterHairColour.Brown,
            CharacterShirtColour.Navy,
            CharacterTrouserColour.Navy,
            CharacterTrouserLength.FullLength,
            CharacterShoeColour.Brown,
            bodySize: bodySize);

        Assert.Equal(bodySize, character.BodySize);
    }

    private static CharacterDefinition CreateDesignedCharacter(
        CharacterPresentation presentation,
        CharacterShirtStyle shirtStyle) => new(
            presentation,
            CharacterSkinTone.Tint1,
            CharacterHairColour.Brown,
            CharacterShirtColour.Navy,
            CharacterTrouserColour.Navy,
            CharacterTrouserLength.FullLength,
            CharacterShoeColour.Brown,
            shirtStyle: shirtStyle);
}
