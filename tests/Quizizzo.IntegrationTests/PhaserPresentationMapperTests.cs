using Quizizzo.Application.Displays;
using Quizizzo.Application.Games;
using Quizizzo.Application.Players;
using Quizizzo.Domain.Players;
using Quizizzo.GameContracts;
using Quizizzo.Web.Presentation;

namespace Quizizzo.IntegrationTests;

public sealed class PhaserPresentationMapperTests
{
    [Fact]
    public void Unpaired_display_maps_to_a_reconstructable_pairing_snapshot()
    {
        var session = new DisplaySessionView(
            Guid.NewGuid(), "PAIR1234", DateTimeOffset.UtcNow.AddMinutes(10), false, null, null);

        var snapshot = PhaserPresentationMapper.Create(session, [], null, null);

        Assert.Equal("Pairing", snapshot.Mode);
        Assert.Equal("Pairing", snapshot.Phase);
        Assert.Empty(snapshot.Players);
        Assert.Empty(snapshot.Results);
    }

    [Fact]
    public void Active_game_snapshot_contains_character_traits_server_scores_and_revealed_results()
    {
        var partyId = Guid.NewGuid();
        var playerId = Guid.NewGuid();
        var session = new DisplaySessionView(
            Guid.NewGuid(), "PAIR1234", DateTimeOffset.UtcNow, true, partyId, "K7XM");
        var player = new PlayerView(
            playerId,
            partyId,
            "K7XM",
            "Player One",
            100,
            PlayerStatus.Disconnected,
            new CharacterView(
                CharacterBodyType.Bean,
                "#4361EE",
                CharacterEyes.Starry,
                CharacterMouth.Grin,
                CharacterAccessory.PartyHat),
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow);
        var payload = new DisplayGameViewPayload(
            "ESTIMATE",
            "A question",
            "Correct answer: 42",
            1,
            1,
            [new GamePresentationEntry(playerId, "Player One", "42", 1, 1000)]);
        var game = new PartyGameView(
            partyId,
            Guid.NewGuid(),
            "estimate",
            GameAudienceRole.Display,
            "Results",
            7,
            null,
            false,
            GameJson.From(payload),
            new Dictionary<Guid, int> { [playerId] = 1100 });

        var snapshot = PhaserPresentationMapper.Create(session, [player], game, payload);
        var mappedPlayer = Assert.Single(snapshot.Players);
        var result = Assert.Single(snapshot.Results);

        Assert.Equal("Game", snapshot.Mode);
        Assert.Equal("estimate", snapshot.GameKey);
        Assert.Equal("Results", snapshot.Phase);
        Assert.Equal(7, snapshot.Revision);
        Assert.Equal(1100, mappedPlayer.Score);
        Assert.Equal("Disconnected", mappedPlayer.Status);
        Assert.Equal("Bean", mappedPlayer.Character.BodyType);
        Assert.Equal("Starry", mappedPlayer.Character.Eyes);
        Assert.Equal("PartyHat", mappedPlayer.Character.Accessory);
        Assert.Equal("Man", mappedPlayer.Character.Presentation);
        Assert.Equal(1, mappedPlayer.Character.SkinTone);
        Assert.Equal(1, result.Rank);
        Assert.Equal(1000, result.PointsAwarded);
    }

    [Fact]
    public void Drawing_presentation_maps_asset_ids_to_local_playback_urls()
    {
        var partyId = Guid.NewGuid();
        var playerId = Guid.NewGuid();
        var assetIds = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        var session = new DisplaySessionView(
            Guid.NewGuid(), "PAIR1234", DateTimeOffset.UtcNow, true, partyId, "K7XM");
        var payload = new DisplayGameViewPayload(
            "AniMates",
            "Vote now",
            "Playback",
            0,
            1,
            [],
            new DrawingPresentationView(
                "Playback",
                150,
                [new DrawingAnimationView(playerId, null, "A prompt", assetIds, 0, null, 0)]));
        var game = new PartyGameView(
            partyId,
            Guid.NewGuid(),
            "animates",
            GameAudienceRole.Display,
            "Voting",
            2,
            DateTimeOffset.UtcNow.AddSeconds(20),
            false,
            GameJson.From(payload),
            new Dictionary<Guid, int>());

        var snapshot = PhaserPresentationMapper.Create(session, [], game, payload);

        var animation = Assert.Single(snapshot.Drawing!.Animations);
        Assert.Equal("Playback", snapshot.Drawing.Mode);
        Assert.Null(animation.CreatorName);
        Assert.Equal(
            assetIds.Select(assetId => $"/api/drawing-assets/{assetId:D}"),
            animation.FrameUrls);
    }
}
