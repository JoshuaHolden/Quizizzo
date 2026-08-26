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
}
