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
        var phaseEndsAtUtc = DateTimeOffset.UtcNow.AddSeconds(20);
        var game = new PartyGameView(
            partyId,
            Guid.NewGuid(),
            "animates",
            GameAudienceRole.Display,
            "Voting",
            2,
            phaseEndsAtUtc,
            false,
            GameJson.From(payload),
            new Dictionary<Guid, int>());

        var snapshot = PhaserPresentationMapper.Create(session, [], game, payload);

        var animation = Assert.Single(snapshot.Drawing!.Animations);
        Assert.Equal("Playback", snapshot.Drawing.Mode);
        Assert.Equal(phaseEndsAtUtc, snapshot.PhaseEndsAtUtc);
        Assert.Equal(1, snapshot.Drawing.LoopsPerAnimation);
        Assert.Null(animation.CreatorName);
        Assert.Equal(
            assetIds.Select(assetId => $"/api/drawing-assets/{assetId:D}"),
            animation.FrameUrls);
    }

    [Fact]
    public void AniMates_briefing_maps_voice_ready_presenter_copy()
    {
        var partyId = Guid.NewGuid();
        var session = new DisplaySessionView(
            Guid.NewGuid(), "PAIR1234", DateTimeOffset.UtcNow, true, partyId, "K7XM");
        var payload = new DisplayGameViewPayload(
            "AniMates", "Everyone gets a secret prompt", "Presenter briefing", 0, 2, [], null,
            new TutorialPresentationView(
                "HOW TO ANIMATE", 3, ["Draw", "Use onion skin", "Undo", "Send"]));
        var game = new PartyGameView(
            partyId, Guid.NewGuid(), "animates", GameAudienceRole.Display, "Briefing", 1, null, false,
            GameJson.From(payload), new Dictionary<Guid, int>());

        var snapshot = PhaserPresentationMapper.Create(session, [], game, payload);

        Assert.Equal(payload.Prompt, snapshot.PresenterMessage);
        Assert.Equal(3, snapshot.Tutorial!.FrameCount);
        Assert.Contains("Use onion skin", snapshot.Tutorial.Steps);
    }

    [Fact]
    public void AniMates_results_map_cumulative_scores_to_podium_ranks_and_drawing_activity()
    {
        var partyId = Guid.NewGuid();
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var session = new DisplaySessionView(
            Guid.NewGuid(), "PAIR1234", DateTimeOffset.UtcNow, true, partyId, "K7XM");
        var players = new[]
        {
            Player(firstId, partyId, "First"),
            Player(secondId, partyId, "Second")
        };
        var payload = new DisplayGameViewPayload(
            "AniMates", "Reveal", "Ready", 2, 2,
            [
                new GamePresentationEntry(firstId, "First", "Thinking", null, 0),
                new GamePresentationEntry(secondId, "Second", "Idle", null, 0)
            ],
            ShowRoundRanking: true);
        var game = new PartyGameView(
            partyId, Guid.NewGuid(), "animates", GameAudienceRole.Display, "Results", 3, null, false,
            GameJson.From(payload), new Dictionary<Guid, int> { [firstId] = 150, [secondId] = 50 });

        var snapshot = PhaserPresentationMapper.Create(session, players, game, payload);

        Assert.True(snapshot.ShowRoundRanking);
        Assert.Equal("Thinking", snapshot.Players.Single(player => player.PlayerId == firstId.ToString("N")).Activity);
        Assert.Equal(1, snapshot.Results.Single(result => result.PlayerId == firstId.ToString("N")).Rank);
        Assert.Equal(2, snapshot.Results.Single(result => result.PlayerId == secondId.ToString("N")).Rank);
    }

    private static PlayerView Player(Guid playerId, Guid partyId, string name) => new(
        playerId, partyId, "K7XM", name, 0, PlayerStatus.Connected,
        new CharacterView(
            CharacterBodyType.Bean, "#4361EE", CharacterEyes.Bright,
            CharacterMouth.Smile, CharacterAccessory.None),
        DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
}
