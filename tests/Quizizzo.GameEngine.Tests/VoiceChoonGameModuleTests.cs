using System.Text.Json;
using Quizizzo.GameContracts;
using Quizizzo.Games.VoiceChoon;

namespace Quizizzo.GameEngine.Tests;

public sealed class VoiceChoonGameModuleTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 4, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Greensleeves_supports_two_players_while_the_full_showdown_still_requires_three()
    {
        var module = new VoiceChoonGameModule();
        var players = Participants().Take(2).ToArray();
        var context = new GameStartContext(
            GameInstanceId.New(), Guid.NewGuid(), "host", players, Now,
            GameJson.From(new VoiceChoonGameConfiguration(SongKey: VoiceChoonSongCatalog.GreensleevesSongKey)));

        var state = module.Start(context).Data.Deserialize<VoiceChoonGameState>()!;

        Assert.Equal(2, state.Participants.Count);
        Assert.Equal("gs.mid", state.SongName);
        var defaultError = Assert.Throws<GameRuleViolationException>(() => module.Start(context with
        {
            Configuration = GameJson.From(new VoiceChoonGameConfiguration())
        }));
        Assert.Equal("invalid-player-count", defaultError.Code);
    }

    [Fact]
    public void Complete_runtime_scores_a_timed_lane_and_keeps_other_charts_private()
    {
        var module = new VoiceChoonGameModule(FastFlow());
        var gameId = GameInstanceId.New();
        var partyId = Guid.NewGuid();
        var players = Participants();
        var state = module.Start(new GameStartContext(gameId, partyId, "host", players, Now));

        state = Deadline(module, state, gameId, partyId);
        Assert.Equal(VoiceChoonGameModule.RecordingPhase, state.Phase);
        foreach (var player in players)
        {
            var recordingState = state.Data.Deserialize<VoiceChoonGameState>()!;
            var participant = recordingState.Participants.Single(item => item.PlayerId == player.PlayerId);
            var prompts = recordingState.Charts.Single(item => item.PlayerIndex == participant.PlayerIndex).RecordingPrompts;
            foreach (var prompt in prompts)
            {
                state = module.Apply(
                    state,
                    Context(gameId, partyId, GameActor.Player(player.PlayerId), Now.AddSeconds(2)),
                    new RegisterVoiceSampleAction(prompt.Key, Guid.NewGuid())).State;
            }
            state = module.Apply(
                state,
                Context(gameId, partyId, GameActor.Player(player.PlayerId), Now.AddSeconds(2)),
                new ConfirmVoiceRecordingsAction()).State;
        }
        Assert.Equal(VoiceChoonGameModule.ControllerReadyPhase, state.Phase);
        foreach (var player in players)
        {
            state = module.Apply(
                state,
                Context(gameId, partyId, GameActor.Player(player.PlayerId), Now.AddSeconds(3)),
                new ReadyVoiceControllerAction()).State;
        }
        Assert.Equal(VoiceChoonGameModule.CountdownPhase, state.Phase);
        state = Deadline(module, state, gameId, partyId);
        Assert.Equal(VoiceChoonGameModule.PlayingPhase, state.Phase);

        var game = state.Data.Deserialize<VoiceChoonGameState>()!;
        var firstPlayer = game.Participants[0];
        var chart = game.Charts.Single(item => item.PlayerIndex == firstPlayer.PlayerIndex);
        var note = chart.Notes.First(item => item.Type == RhythmNoteType.Tap);
        var hitAt = game.SongStartsAtUtc!.Value.AddSeconds(note.StartTimeSeconds);
        var transition = module.Apply(
            state,
            Context(gameId, partyId, GameActor.Player(firstPlayer.PlayerId), hitAt),
            new SubmitVoiceInputAction(1, note.Lane, hitAt));

        var scored = transition.State.Data.Deserialize<VoiceChoonGameState>()!;
        Assert.Equal(1000, scored.ScoresByPlayer[firstPlayer.PlayerId]);
        Assert.Equal(VoiceNoteRating.Perfect, Assert.Single(scored.JudgementsByPlayer[firstPlayer.PlayerId]).Rating);
        Assert.Contains(transition.Events, item => item.Kind == "VoiceNoteJudged");

        var playerView = module.CreateView(
            transition.State,
            new GameViewContext(GameAudienceRole.Player, firstPlayer.PlayerId.ToString("N"), firstPlayer.PlayerId));
        var payload = playerView.Data.Deserialize<PlayerGameViewPayload>()!;
        var privateState = payload.State.Deserialize<VoiceChoonPlayerState>()!;
        Assert.Equal(chart.Notes.Count, privateState.Chart.Notes.Count);
        Assert.DoesNotContain(
            scored.Charts.Where(item => item.PlayerIndex != firstPlayer.PlayerIndex).SelectMany(item => item.Notes),
            item => privateState.Chart.Notes.Any(own => own.Id == item.Id));
    }

    [Fact]
    public void Hold_requires_a_release_and_a_quick_tap_cannot_receive_full_credit()
    {
        var module = new VoiceChoonGameModule(FastFlow());
        var gameId = GameInstanceId.New();
        var partyId = Guid.NewGuid();
        var players = Participants();
        var started = module.Start(new GameStartContext(gameId, partyId, "host", players, Now));
        var game = started.Data.Deserialize<VoiceChoonGameState>()!;
        var chart = game.Charts.First(item => item.Notes.Any(note => note.Type == RhythmNoteType.Hold));
        var note = chart.Notes.First(item => item.Type == RhythmNoteType.Hold);
        var playerId = game.Participants.Single(item => item.PlayerIndex == chart.PlayerIndex).PlayerId;
        var playing = started with
        {
            Phase = VoiceChoonGameModule.PlayingPhase,
            PhaseEndsAtUtc = Now.AddMinutes(3),
            Data = GameJson.From(game with { SongStartsAtUtc = Now })
        };
        var pressedAt = Now.AddSeconds(note.StartTimeSeconds);

        var pressed = module.Apply(playing,
            Context(gameId, partyId, GameActor.Player(playerId), pressedAt),
            new SubmitVoiceInputAction(1, note.Lane, pressedAt));
        var whileHeld = pressed.State.Data.Deserialize<VoiceChoonGameState>()!;

        Assert.Empty(whileHeld.JudgementsByPlayer[playerId]);
        Assert.True(whileHeld.ActiveHoldsByPlayer!.ContainsKey(playerId));

        var released = module.Apply(pressed.State,
            Context(gameId, partyId, GameActor.Player(playerId), pressedAt.AddMilliseconds(50)),
            new SubmitVoiceInputAction(2, note.Lane, pressedAt.AddMilliseconds(50), Released: true));
        var afterRelease = released.State.Data.Deserialize<VoiceChoonGameState>()!;

        Assert.InRange(afterRelease.ScoresByPlayer[playerId], 1, 999);
        Assert.Empty(afterRelease.ActiveHoldsByPlayer!);
        Assert.Contains(released.Events, item => item.Kind == "VoiceHoldJudged");
    }

    [Fact]
    public void Recording_gate_requires_every_server_assigned_prompt()
    {
        var module = new VoiceChoonGameModule(FastFlow());
        var gameId = GameInstanceId.New();
        var partyId = Guid.NewGuid();
        var players = Participants();
        var state = module.Start(new GameStartContext(gameId, partyId, "host", players, Now));
        state = Deadline(module, state, gameId, partyId);

        var error = Assert.Throws<GameRuleViolationException>(() => module.Apply(
            state,
            Context(gameId, partyId, GameActor.Player(players[0].PlayerId), Now.AddSeconds(1)),
            new ConfirmVoiceRecordingsAction()));

        Assert.Equal("recordings-incomplete", error.Code);
        var payload = module.CreateView(
            state,
            new GameViewContext(GameAudienceRole.Player, players[0].PlayerId.ToString("N"), players[0].PlayerId))
            .Data.Deserialize<PlayerGameViewPayload>()!;
        Assert.Equal(PlayerControllerKind.Recording, payload.Controller.Kind);
        var prompts = payload.Controller.Configuration.Deserialize<RecordingControllerConfiguration>()!.Prompts;
        Assert.NotEmpty(prompts);
        Assert.All(prompts, prompt => Assert.Null(prompt.AssetId));
    }

    [Fact]
    public void Results_convert_each_positive_performance_to_party_score_once()
    {
        var module = new VoiceChoonGameModule(FastFlow());
        var gameId = GameInstanceId.New();
        var partyId = Guid.NewGuid();
        var players = Participants();
        var started = module.Start(new GameStartContext(gameId, partyId, "host", players, Now));
        var game = started.Data.Deserialize<VoiceChoonGameState>()!;
        var scored = game with
        {
            ScoresByPlayer = new Dictionary<Guid, int>
            {
                [players[0].PlayerId] = 2000,
                [players[1].PlayerId] = 750,
                [players[2].PlayerId] = 0
            }
        };
        var playing = started with
        {
            Phase = VoiceChoonGameModule.PlayingPhase,
            PhaseEndsAtUtc = Now.AddMinutes(2),
            Data = GameJson.From(scored)
        };

        var results = Deadline(module, playing, gameId, partyId);
        Assert.Equal(VoiceChoonGameModule.ResultsPhase, results.Phase);
        var completed = module.Apply(
            results,
            Context(gameId, partyId, GameActor.SystemActor, results.PhaseEndsAtUtc!.Value),
            new DeadlineElapsedAction(results.PhaseEndsAtUtc.Value));

        Assert.True(completed.State.IsComplete);
        Assert.Equal([2000, 750], completed.ScoreAwards.Select(item => item.Points));
        Assert.Contains(completed.Events, item => item.Kind == "GameCompleted");
    }

    [Fact]
    public void Inputs_reject_wrong_players_stale_sequences_and_wrong_lanes()
    {
        var module = new VoiceChoonGameModule(FastFlow());
        var gameId = GameInstanceId.New();
        var partyId = Guid.NewGuid();
        var players = Participants();
        var started = module.Start(new GameStartContext(gameId, partyId, "host", players, Now));
        var game = started.Data.Deserialize<VoiceChoonGameState>()!;
        var playing = started with
        {
            Phase = VoiceChoonGameModule.PlayingPhase,
            PhaseEndsAtUtc = Now.AddMinutes(2),
            Data = GameJson.From(game with { SongStartsAtUtc = Now })
        };

        Assert.Equal("player-required", Assert.Throws<GameRuleViolationException>(() => module.Apply(
            playing,
            Context(gameId, partyId, GameActor.Player(Guid.NewGuid()), Now),
            new SubmitVoiceInputAction(1, 0, Now))).Code);
        Assert.Equal("invalid-lane", Assert.Throws<GameRuleViolationException>(() => module.Apply(
            playing,
            Context(gameId, partyId, GameActor.Player(players[0].PlayerId), Now),
            new SubmitVoiceInputAction(1, 4, Now))).Code);
        Assert.Equal("stale-input", Assert.Throws<GameRuleViolationException>(() => module.Apply(
            playing,
            Context(gameId, partyId, GameActor.Player(players[0].PlayerId), Now),
            new SubmitVoiceInputAction(0, 0, Now))).Code);
    }

    [Fact]
    public void Decoder_accepts_the_existing_arcade_submission_shape()
    {
        var module = new VoiceChoonGameModule();
        var decoded = Assert.IsType<SubmitVoiceInputAction>(module.DecodeAction(
            SubmitVoiceInputAction.ActionKind,
            JsonSerializer.SerializeToElement(new
            {
                sequence = 7,
                input = "Lane2",
                targetPlayerId = (string?)null,
                clientTimestamp = Now
            })));

        Assert.Equal(7, decoded.Sequence);
        Assert.Equal(2, decoded.Lane);
        Assert.Equal(Now, decoded.ClientTimestamp);
    }

    [Theory]
    [InlineData(VoiceChoonDifficulty.Easy, 0.3)]
    [InlineData(VoiceChoonDifficulty.Medium, 0.25)]
    [InlineData(VoiceChoonDifficulty.Hard, 0.2)]
    public void Difficulty_is_reconstructable_and_controls_the_authoritative_hit_window(
        VoiceChoonDifficulty difficulty,
        double expectedGoodWindowSeconds)
    {
        var module = new VoiceChoonGameModule(FastFlow());
        var gameId = GameInstanceId.New();
        var partyId = Guid.NewGuid();
        var players = Participants();
        var started = module.Start(new GameStartContext(
            gameId,
            partyId,
            "host",
            players,
            Now,
            GameJson.From(new VoiceChoonGameConfiguration(difficulty))));
        var game = started.Data.Deserialize<VoiceChoonGameState>()!;
        var playing = started with
        {
            Phase = VoiceChoonGameModule.PlayingPhase,
            PhaseEndsAtUtc = Now.AddMinutes(2),
            Data = GameJson.From(game with { SongStartsAtUtc = Now })
        };
        var playerId = players[0].PlayerId;
        var payload = module.CreateView(
            playing,
            new GameViewContext(GameAudienceRole.Player, playerId.ToString("N"), playerId))
            .Data.Deserialize<PlayerGameViewPayload>()!;
        var controller = payload.Controller.Configuration.Deserialize<RhythmControllerConfiguration>()!;

        Assert.Equal(difficulty, game.Difficulty);
        Assert.Equal(expectedGoodWindowSeconds, controller.GoodWindowSeconds);
        Assert.Equal(game.Charts[0].Notes.Count, controller.Notes.Count);
    }

    [Fact]
    public void Wubquake_song_selection_reconstructs_song_and_player_guidance()
    {
        var module = new VoiceChoonGameModule(FastFlow());
        var gameId = GameInstanceId.New();
        var partyId = Guid.NewGuid();
        var player = Participants()[0];
        var started = module.Start(new GameStartContext(
            gameId,
            partyId,
            "host",
            [player],
            Now,
            GameJson.From(new VoiceChoonGameConfiguration(
                VoiceChoonDifficulty.Medium,
                true,
                VoiceChoonSongCatalog.WubquakeSongKey))));
        var game = started.Data.Deserialize<VoiceChoonGameState>()!;
        var payload = module.CreateView(
            started,
            new GameViewContext(GameAudienceRole.Player, player.PlayerId.ToString("N"), player.PlayerId))
            .Data.Deserialize<PlayerGameViewPayload>()!;

        Assert.Equal(VoiceChoonSongCatalog.WubquakeSongKey, game.SongKey);
        Assert.Equal("quizizzo_wubquake.mid", game.SongName);
        Assert.Contains("bass-heavy", payload.Instructions, StringComparison.OrdinalIgnoreCase);
        Assert.All(game.Charts.SelectMany(chart => chart.RecordingPrompts), prompt =>
            Assert.Contains("mouth", prompt.Guidance, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Invalid_difficulty_is_rejected_server_side()
    {
        var module = new VoiceChoonGameModule();
        var context = new GameStartContext(
            GameInstanceId.New(),
            Guid.NewGuid(),
            "host",
            Participants(),
            Now,
            GameJson.From(new VoiceChoonGameConfiguration((VoiceChoonDifficulty)99)));

        var error = Assert.Throws<GameRuleViolationException>(() => module.Start(context));

        Assert.Equal("invalid-configuration", error.Code);
    }

    [Fact]
    public void Unknown_song_is_rejected_server_side()
    {
        var module = new VoiceChoonGameModule();
        var context = new GameStartContext(
            GameInstanceId.New(),
            Guid.NewGuid(),
            "host",
            Participants(),
            Now,
            GameJson.From(new VoiceChoonGameConfiguration(SongKey: "missing-song")));

        var error = Assert.Throws<GameRuleViolationException>(() => module.Start(context));

        Assert.Equal("invalid-configuration", error.Code);
    }

    [Fact]
    public void One_player_requires_explicit_solo_autoplay_test_mode()
    {
        var module = new VoiceChoonGameModule(FastFlow());
        var soloPlayer = Participants()[..1];
        var normalContext = new GameStartContext(
            GameInstanceId.New(), Guid.NewGuid(), "host", soloPlayer, Now);

        var error = Assert.Throws<GameRuleViolationException>(() => module.Start(normalContext));

        Assert.Equal("invalid-player-count", error.Code);
    }

    [Fact]
    public void Solo_autoplay_test_scores_every_note_perfectly_without_player_input()
    {
        var module = new VoiceChoonGameModule(FastFlow());
        var gameId = GameInstanceId.New();
        var partyId = Guid.NewGuid();
        var player = Participants()[0];
        var state = module.Start(new GameStartContext(
            gameId,
            partyId,
            "host",
            [player],
            Now,
            GameJson.From(new VoiceChoonGameConfiguration(
                VoiceChoonDifficulty.Medium,
                SoloAutoplayTest: true))));

        state = Deadline(module, state, gameId, partyId);
        state = Deadline(module, state, gameId, partyId);
        state = Deadline(module, state, gameId, partyId);
        state = Deadline(module, state, gameId, partyId);

        var game = state.Data.Deserialize<VoiceChoonGameState>()!;
        var chart = Assert.Single(game.Charts);
        var sampleAssets = chart.RecordingPrompts.ToDictionary(prompt => prompt.Key, _ => Guid.NewGuid());
        game = game with
        {
            SampleAssetIdsByPlayer = new Dictionary<Guid, IReadOnlyDictionary<string, Guid>>
            {
                [player.PlayerId] = sampleAssets
            }
        };
        state = state with { Data = GameJson.From(game) };
        var judgements = Assert.Single(game.JudgementsByPlayer).Value;
        var payload = module.CreateView(
            state,
            new GameViewContext(GameAudienceRole.Player, player.PlayerId.ToString("N"), player.PlayerId))
            .Data.Deserialize<PlayerGameViewPayload>()!;
        var controller = payload.Controller.Configuration.Deserialize<RhythmControllerConfiguration>()!;

        Assert.Equal(VoiceChoonGameModule.PlayingPhase, state.Phase);
        Assert.True(game.SoloAutoplayTest);
        Assert.Equal(18, chart.RecordingPrompts.Count);
        Assert.Equal(chart.Notes.Count, judgements.Count);
        Assert.All(judgements, judgement => Assert.Equal(VoiceNoteRating.Perfect, judgement.Rating));
        Assert.Equal(chart.Notes.Count * 1000, game.ScoresByPlayer[player.PlayerId]);
        Assert.True(controller.Autoplay);
        Assert.Equal(chart.PlaybackNotes.Count, controller.Notes.Count);
        Assert.True(controller.Notes.Count > chart.Notes.Count);
        Assert.All(controller.Notes, note => Assert.NotEmpty(note.SoundLabel));
        Assert.All(controller.Notes, controllerNote =>
        {
            var sourceNote = chart.PlaybackNotes.Single(note => note.Id == controllerNote.Id);
            var expectedAssets = InstrumentSoundGuide.For(sourceNote.SourceRole)
                .Select(prompt => sampleAssets[prompt.Key]);
            Assert.Contains(controllerNote.SampleAssetId!.Value, expectedAssets);
        });
        Assert.Equal("autoplay-active", Assert.Throws<GameRuleViolationException>(() => module.Apply(
            state,
            Context(gameId, partyId, GameActor.Player(player.PlayerId), Now),
            new SubmitVoiceInputAction(1, 0, Now))).Code);
    }

    private static GameModuleState Deadline(
        VoiceChoonGameModule module,
        GameModuleState state,
        GameInstanceId gameId,
        Guid partyId) => module.Apply(
            state,
            Context(gameId, partyId, GameActor.SystemActor, state.PhaseEndsAtUtc!.Value),
            new DeadlineElapsedAction(state.PhaseEndsAtUtc.Value)).State;

    private static GameActionContext Context(
        GameInstanceId gameId,
        Guid partyId,
        GameActor actor,
        DateTimeOffset at) => new(gameId, partyId, actor, at);

    private static GameParticipant[] Participants() =>
    [
        new(Guid.Parse("10000000-0000-0000-0000-000000000001"), "Ada"),
        new(Guid.Parse("10000000-0000-0000-0000-000000000002"), "Bea"),
        new(Guid.Parse("10000000-0000-0000-0000-000000000003"), "Cy")
    ];

    private static VoiceChoonFlowOptions FastFlow() => new()
    {
        BriefingDuration = TimeSpan.FromMilliseconds(10),
        RecordingDuration = TimeSpan.FromMilliseconds(10),
        ControllerReadyDuration = TimeSpan.FromMilliseconds(10),
        CountdownDuration = TimeSpan.FromMilliseconds(10),
        ResultsDuration = TimeSpan.FromMilliseconds(10)
    };
}
