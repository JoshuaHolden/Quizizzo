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
        Assert.Null(snapshot.JoinUrl);
        Assert.Empty(snapshot.Players);
        Assert.Empty(snapshot.Results);
    }

    [Fact]
    public void Active_game_snapshot_contains_character_traits_server_scores_and_revealed_results()
    {
        var partyId = Guid.NewGuid();
        var playerId = Guid.NewGuid();
        var gameInstanceId = Guid.NewGuid();
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
            gameInstanceId,
            "estimate",
            GameAudienceRole.Display,
            "Results",
            7,
            null,
            false,
            GameJson.From(payload),
            new Dictionary<Guid, int> { [playerId] = 1100 });

        var snapshot = PhaserPresentationMapper.Create(
            session, [player], game, payload,
            "https://quizizzo.com/join/K7XM", "data:image/png;base64,abc");
        var mappedPlayer = Assert.Single(snapshot.Players);
        var result = Assert.Single(snapshot.Results);

        Assert.Equal("Game", snapshot.Mode);
        Assert.Equal("estimate", snapshot.GameKey);
        Assert.Equal(gameInstanceId.ToString("N"), snapshot.GameInstanceId);
        Assert.Equal("Results", snapshot.Phase);
        Assert.Equal("K7XM", snapshot.RoomCode);
        Assert.Equal("https://quizizzo.com/join/K7XM", snapshot.JoinUrl);
        Assert.Equal("data:image/png;base64,abc", snapshot.JoinQrDataUri);
        Assert.Equal(payload.Title, snapshot.Title);
        Assert.Equal(payload.Prompt, snapshot.Prompt);
        Assert.Equal(payload.PhaseMessage, snapshot.PhaseMessage);
        Assert.Equal(payload.Entries[0].Label, Assert.Single(snapshot.Entries).Label);
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
            ShowRoundRanking: true,
            Statistics:
            [
                new GameStatisticView("FASTEST ANIMATOR", "First · 12.4s average"),
                new GameStatisticView("MOST LOVED ANIMATION", "Second · 3 votes")
            ]);
        var game = new PartyGameView(
            partyId, Guid.NewGuid(), "animates", GameAudienceRole.Display, "Results", 3, null, false,
            GameJson.From(payload), new Dictionary<Guid, int> { [firstId] = 150, [secondId] = 50 });

        var snapshot = PhaserPresentationMapper.Create(session, players, game, payload);

        Assert.True(snapshot.ShowRoundRanking);
        Assert.Equal("Thinking", snapshot.Players.Single(player => player.PlayerId == firstId.ToString("N")).Activity);
        Assert.Equal(1, snapshot.Results.Single(result => result.PlayerId == firstId.ToString("N")).Rank);
        Assert.Equal(2, snapshot.Results.Single(result => result.PlayerId == secondId.ToString("N")).Rank);
        Assert.Equal("FASTEST ANIMATOR", snapshot.Statistics![0].Label);
        Assert.Equal("Second · 3 votes", snapshot.Statistics[1].Value);
    }

    [Fact]
    public void Slop_machine_maps_thumbnail_media_view_units_and_round_score_deltas()
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
            "SLOP MACHINE", "CURRENT CHANNEL RANKINGS", "The algorithm refreshed.", 0, 2,
            [
                new GamePresentationEntry(firstId, "First", "5,000 views", 1, 3000),
                new GamePresentationEntry(secondId, "Second", "2,000 views", 2, 0)
            ],
            ShowRoundRanking: true,
            Media: new GameMediaPresentationView("hero",
            [
                new GameMediaItem("cb-1", "/media/games/slop-machine/thumbnails/cb-1.webp",
                    "A ridiculous thumbnail", "THE UPLOAD", "A title", "VIRAL")
            ]),
            ScoreUnit: "views");
        var game = new PartyGameView(
            partyId, Guid.NewGuid(), "slop-machine", GameAudienceRole.Display,
            "ScoreReview1", 4, null, false, GameJson.From(payload),
            new Dictionary<Guid, int> { [firstId] = 5000, [secondId] = 2000 });

        var snapshot = PhaserPresentationMapper.Create(session, players, game, payload);

        Assert.Equal("views", snapshot.ScoreUnit);
        Assert.Equal("hero", snapshot.Media!.Mode);
        Assert.Equal("A ridiculous thumbnail", Assert.Single(snapshot.Media.Items).AlternativeText);
        Assert.Equal(3000,
            snapshot.Results.Single(result => result.PlayerId == firstId.ToString("N")).PointsAwarded);
        Assert.Equal(0,
            snapshot.Results.Single(result => result.PlayerId == secondId.ToString("N")).PointsAwarded);
    }

    [Fact]
    public void Machine_owned_and_repeated_entries_do_not_break_the_display_snapshot()
    {
        var partyId = Guid.NewGuid();
        var playerId = Guid.NewGuid();
        var session = new DisplaySessionView(
            Guid.NewGuid(), "PAIR1234", DateTimeOffset.UtcNow, true, partyId, "K7XM");
        var players = new[] { Player(playerId, partyId, "Player") };
        var payload = new DisplayGameViewPayload(
            "SLOP MACHINE", "VOTE", "Pick one", 0, 1,
            [
                new GamePresentationEntry(Guid.Empty, "A", "Machine title one", 1, 0),
                new GamePresentationEntry(Guid.Empty, "B", "Machine title two", 2, 0),
                new GamePresentationEntry(playerId, "C", "Human title one", 3, 100),
                new GamePresentationEntry(playerId, "D", "Human title two", 4, 100)
            ]);
        var game = new PartyGameView(
            partyId, Guid.NewGuid(), "slop-machine", GameAudienceRole.Display,
            "FreshResults", 5, null, false, GameJson.From(payload),
            new Dictionary<Guid, int> { [playerId] = 100 });

        var snapshot = PhaserPresentationMapper.Create(session, players, game, payload);

        Assert.Equal("Human title two", Assert.Single(snapshot.Players).Activity);
        var result = Assert.Single(snapshot.Results);
        Assert.Equal(playerId.ToString("N"), result.PlayerId);
        Assert.Equal(3, result.Rank);
        Assert.DoesNotContain(snapshot.Results, item => item.PlayerId == Guid.Empty.ToString("N"));
    }

    private static PlayerView Player(Guid playerId, Guid partyId, string name) => new(
        playerId, partyId, "K7XM", name, 0, PlayerStatus.Connected,
        new CharacterView(
            CharacterBodyType.Bean, "#4361EE", CharacterEyes.Bright,
            CharacterMouth.Smile, CharacterAccessory.None),
        DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
}
