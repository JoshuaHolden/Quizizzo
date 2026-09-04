using System.Text.Json;
using Quizizzo.GameContracts;
using Quizizzo.GameEngine;
using Quizizzo.Games.PileUpPanic;

namespace Quizizzo.GameEngine.Tests;

public sealed class PileUpPanicGameModuleTests
{
    [Fact]
    public async Task Runtime_serializes_inputs_and_preserves_sequence_across_actor_recovery()
    {
        var module = new PileUpPanicGameModule(
            new PileUpOptions { RoundDuration = TimeSpan.FromMinutes(2) },
            TimeSpan.FromSeconds(1),
            FastFlow());
        var store = new InMemoryGameStateStore();
        var gameId = GameInstanceId.New();
        var partyId = Guid.NewGuid();
        var players = Participants(2);
        await using var runtime = Runtime(module, store);
        await runtime.StartAsync(new GameStartRequest(gameId, partyId, "host", module.Descriptor.Key, players));
        await WaitForPhaseAsync(runtime, gameId, PileUpPanicGameModule.PlayingPhase);

        var commandId = GameCommandId.New();
        var command = new GameCommand(
            commandId,
            gameId,
            partyId,
            GameActor.Player(players[0].PlayerId),
            new SubmitPileInputAction(0, PileInputType.MoveLeft, null, DateTimeOffset.UnixEpoch));
        var applied = await runtime.ExecuteAsync(command);
        var idempotent = await runtime.ExecuteAsync(command);
        await runtime.ReleaseAsync(gameId);
        var stale = await runtime.ExecuteAsync(command with
        {
            CommandId = GameCommandId.New(),
            Action = new SubmitPileInputAction(0, PileInputType.MoveRight, null, DateTimeOffset.UnixEpoch)
        });
        var continued = await runtime.ExecuteAsync(command with
        {
            CommandId = GameCommandId.New(),
            Action = new SubmitPileInputAction(1, PileInputType.MoveRight, null, DateTimeOffset.UnixEpoch)
        });

        Assert.Equal(GameCommandOutcome.Applied, applied.Outcome);
        Assert.True(idempotent.IsDuplicate);
        Assert.Equal("duplicate-input", stale.ErrorCode);
        Assert.Equal(GameCommandOutcome.Applied, continued.Outcome);
        var persisted = await store.LoadAsync(gameId);
        var game = persisted!.ModuleState.Data.Deserialize<PileUpGameState>();
        Assert.Equal(1, game!.Match.Players.Single(player => player.Arena.PlayerId == players[0].PlayerId).LastSequence);
    }

    [Fact]
    public async Task Recurring_runtime_recovers_mid_round_without_timeout_completion()
    {
        var module = new PileUpPanicGameModule(
            new PileUpOptions
            {
                RoundDuration = TimeSpan.FromMinutes(2),
                DisconnectGracePeriod = TimeSpan.FromMilliseconds(40),
                SimulationStep = TimeSpan.FromMilliseconds(20),
                InitialFallInterval = TimeSpan.FromSeconds(1),
                MinimumFallInterval = TimeSpan.FromMilliseconds(100)
            },
            TimeSpan.FromMilliseconds(25),
            FastFlow());
        var store = new InMemoryGameStateStore();
        var gameId = GameInstanceId.New();
        var partyId = Guid.NewGuid();
        var players = Participants(2);
        await using var runtime = Runtime(module, store);
        await runtime.StartAsync(new GameStartRequest(gameId, partyId, "host", module.Descriptor.Key, players));
        await WaitForPhaseAsync(runtime, gameId, PileUpPanicGameModule.PlayingPhase);
        var playingRevision = (await runtime.GetStatusAsync(gameId)).Revision;
        await WaitForRevisionAsync(runtime, gameId, playingRevision + 1);

        await runtime.ReleaseAsync(gameId);
        _ = await runtime.GetViewAsync(gameId, GameViewRequest.Display("display"));
        await Task.Delay(250);
        var status = await runtime.GetStatusAsync(gameId);
        var persisted = await store.LoadAsync(gameId);
        var game = persisted!.ModuleState.Data.Deserialize<PileUpGameState>();

        Assert.False(status.IsComplete, $"Unexpectedly completed in {status.Phase} at revision {status.Revision}.");
        Assert.Equal(PileUpPanicGameModule.PlayingPhase, status.Phase);
        Assert.Equal(1, game!.RoundNumber);
    }

    [Fact]
    public async Task Player_view_contains_own_arena_but_only_bounded_opponent_summaries()
    {
        var module = new PileUpPanicGameModule(
            simulationInterval: TimeSpan.FromSeconds(1),
            flowOptions: FastFlow());
        var gameId = GameInstanceId.New();
        var players = Participants(3);
        await using var runtime = Runtime(module, new InMemoryGameStateStore());
        await runtime.StartAsync(new GameStartRequest(gameId, Guid.NewGuid(), "host", module.Descriptor.Key, players));
        await WaitForPhaseAsync(runtime, gameId, PileUpPanicGameModule.PlayingPhase);

        var view = await runtime.GetViewAsync(gameId, GameViewRequest.Player(players[0].PlayerId));
        var payload = view.Data.Deserialize<PlayerGameViewPayload>();
        var playerState = payload!.State.Deserialize<PilePlayerViewState>();

        Assert.Equal(players[0].PlayerId, playerState!.Arena.PlayerId);
        Assert.Equal(2, playerState.Opponents.Count);
        Assert.DoesNotContain(playerState.Opponents, opponent => opponent.PlayerId == players[0].PlayerId);
        Assert.DoesNotContain("\"grid\"", payload.State.GetRawText(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"upcoming\"", payload.State.GetRawText(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(PlayerControllerKind.Arcade, payload.Controller.Kind);
        Assert.Equal("pts", payload.ScoreUnit);
        var controls = payload.Controller.Configuration.Deserialize<ArcadeControllerConfiguration>();
        Assert.Equal(6, controls!.Controls.Count);
        Assert.Equal(2, controls.Targets.Count);
        Assert.All(controls.Targets, target => Assert.Null(target.Detail));
        Assert.Equal(0, controls.NextSequence);
        Assert.NotNull(controls.Arena);
        Assert.Equal(PileUpOptions.Columns, controls.Arena.Columns);
        Assert.Equal(PileUpOptions.VisibleRows, controls.Arena.VisibleRows);
        Assert.Equal(2, controls.Arena.UpcomingPieces.Count);
        Assert.Equal(ScrapClusterCatalogue.All.Count, controls.Arena.PieceShapes.Count);
        Assert.All(playerState.Opponents, opponent => Assert.False(opponent.IsOverloaded));

        var display = await runtime.GetViewAsync(gameId, GameViewRequest.Display("display"));
        var displayPayload = display.Data.Deserialize<DisplayGameViewPayload>();
        var displayState = displayPayload!.State!.Value.Deserialize<PileDisplayViewState>();
        Assert.Equal(3, displayState!.Match.Arenas.Count);
        Assert.All(displayState.Match.Arenas, arena => Assert.NotNull(arena.Grid));
    }

    [Fact]
    public void Every_controller_ready_advances_early_through_reveal_and_countdown()
    {
        var now = new DateTimeOffset(2026, 9, 3, 15, 0, 0, TimeSpan.Zero);
        var module = new PileUpPanicGameModule(flowOptions: FastFlow());
        var players = Participants(2);
        var gameId = GameInstanceId.New();
        var partyId = Guid.NewGuid();
        var state = module.Start(new GameStartContext(gameId, partyId, "host", players, now));
        Assert.Equal(PileUpPanicGameModule.IntroductionPhase, state.Phase);

        state = module.Apply(
            state,
            Context(gameId, partyId, GameActor.SystemActor, state.PhaseEndsAtUtc!.Value),
            new DeadlineElapsedAction(state.PhaseEndsAtUtc.Value)).State;
        Assert.Equal(PileUpPanicGameModule.ControllerReadyPhase, state.Phase);
        var readyDeadline = state.PhaseEndsAtUtc;

        state = module.Apply(
            state,
            Context(gameId, partyId, GameActor.Player(players[0].PlayerId), now.AddSeconds(1)),
            new ReadyPileControllerAction()).State;
        Assert.Equal(PileUpPanicGameModule.ControllerReadyPhase, state.Phase);
        state = module.Apply(
            state,
            Context(gameId, partyId, GameActor.Player(players[1].PlayerId), now.AddSeconds(1)),
            new ReadyPileControllerAction()).State;
        Assert.Equal(PileUpPanicGameModule.ArenaRevealPhase, state.Phase);
        Assert.NotEqual(readyDeadline, state.PhaseEndsAtUtc);

        state = AdvanceDeadline(module, state, gameId, partyId);
        Assert.Equal(PileUpPanicGameModule.CountdownPhase, state.Phase);
        state = AdvanceDeadline(module, state, gameId, partyId);
        Assert.Equal(PileUpPanicGameModule.PlayingPhase, state.Phase);
        Assert.Equal(
            state.Data.Deserialize<PileUpGameState>()!.Match.RoundEndsAtUtc,
            state.PhaseEndsAtUtc);

        var player = module.CreateView(
            state,
            new GameViewContext(GameAudienceRole.Player, players[0].PlayerId.ToString("N"), players[0].PlayerId));
        Assert.Equal(
            PlayerControllerKind.Arcade,
            player.Data.Deserialize<PlayerGameViewPayload>()!.Controller.Kind);
    }

    [Fact]
    public void Decided_match_runs_winner_reveal_celebration_and_terminal_scoring_once()
    {
        var now = new DateTimeOffset(2026, 9, 3, 15, 0, 0, TimeSpan.Zero);
        var module = new PileUpPanicGameModule(flowOptions: FastFlow());
        var players = Participants(2);
        var gameId = GameInstanceId.New();
        var partyId = Guid.NewGuid();
        var started = module.Start(new GameStartContext(gameId, partyId, "host", players, now));
        var game = started.Data.Deserialize<PileUpGameState>()!;
        var winnerId = players[0].PlayerId;
        var loserId = players[1].PlayerId;
        game = game with
        {
            RoundWins = new Dictionary<Guid, int> { [winnerId] = 2, [loserId] = 0 },
            RoundPoints = new Dictionary<Guid, int> { [winnerId] = 8, [loserId] = 4 },
            PerformanceViews = new Dictionary<Guid, int> { [winnerId] = 900, [loserId] = 300 },
            Results =
            [
                new PileRoundStanding(winnerId, players[0].DisplayName, 1, 500, 2, 4),
                new PileRoundStanding(loserId, players[1].DisplayName, 2, 200, 0, 2)
            ]
        };
        var standings = started with
        {
            Phase = PileUpPanicGameModule.StandingsPhase,
            PhaseEndsAtUtc = now.AddSeconds(1),
            Data = GameJson.From(game)
        };

        var winnerReveal = AdvanceDeadline(module, standings, gameId, partyId);
        Assert.Equal(PileUpPanicGameModule.FinalWinnerPhase, winnerReveal.Phase);
        var revealed = winnerReveal.Data.Deserialize<PileUpGameState>()!;
        Assert.Equal(winnerId, revealed.MatchWinnerId);
        Assert.Equal(3900, revealed.FinalViews[winnerId]);
        Assert.Equal(1300, revealed.FinalViews[loserId]);

        var celebration = module.Apply(
            winnerReveal,
            Context(gameId, partyId, GameActor.SystemActor, winnerReveal.PhaseEndsAtUtc!.Value),
            new DeadlineElapsedAction(winnerReveal.PhaseEndsAtUtc.Value));
        Assert.Equal(PileUpPanicGameModule.WinnerCelebrationPhase, celebration.State.Phase);
        Assert.Empty(celebration.ScoreAwards);

        var completed = module.Apply(
            celebration.State,
            Context(gameId, partyId, GameActor.SystemActor, celebration.State.PhaseEndsAtUtc!.Value),
            new DeadlineElapsedAction(celebration.State.PhaseEndsAtUtc.Value));
        Assert.True(completed.State.IsComplete);
        Assert.Equal(PileUpPanicGameModule.CompletedPhase, completed.State.Phase);
        Assert.Equal(2, completed.ScoreAwards.Count);
        Assert.Contains(completed.Events, item => item.Kind == "PileMatchCompleted");
    }

    [Fact]
    public void Serialized_presence_changes_disable_control_and_overload_into_spectator_mode()
    {
        var now = new DateTimeOffset(2026, 9, 3, 15, 0, 0, TimeSpan.Zero);
        var module = new PileUpPanicGameModule(
            new PileUpOptions { DisconnectGracePeriod = TimeSpan.FromSeconds(2) },
            flowOptions: FastFlow());
        var players = Participants(3);
        var gameId = GameInstanceId.New();
        var partyId = Guid.NewGuid();
        var started = module.Start(new GameStartContext(gameId, partyId, "host", players, now));
        var game = started.Data.Deserialize<PileUpGameState>()!;
        var playing = started with
        {
            Phase = PileUpPanicGameModule.PlayingPhase,
            PhaseEndsAtUtc = game.Match.RoundEndsAtUtc
        };

        var disconnected = module.Apply(
            playing,
            Context(gameId, partyId, GameActor.SystemActor, now),
            new PlayerPresenceChangedAction(players[0].PlayerId, false));
        var disconnectedPayload = module.CreateView(
            disconnected.State,
            new GameViewContext(
                GameAudienceRole.Player,
                players[0].PlayerId.ToString("N"),
                players[0].PlayerId)).Data.Deserialize<PlayerGameViewPayload>();
        Assert.Equal(PlayerControllerKind.Arcade, disconnectedPayload!.Controller.Kind);
        Assert.False(disconnectedPayload.Controller.IsEnabled);

        var simulated = module.Apply(
            disconnected.State,
            Context(gameId, partyId, GameActor.SystemActor, now.AddSeconds(3)),
            new SimulationTickElapsedAction(now.AddSeconds(3)));
        var spectatorPayload = module.CreateView(
            simulated.State,
            new GameViewContext(
                GameAudienceRole.Player,
                players[0].PlayerId.ToString("N"),
                players[0].PlayerId)).Data.Deserialize<PlayerGameViewPayload>();
        var spectatorState = spectatorPayload!.State.Deserialize<PilePlayerViewState>();

        Assert.True(spectatorState!.Arena.IsOverloaded);
        Assert.Equal(PlayerControllerKind.Waiting, spectatorPayload.Controller.Kind);
        Assert.Contains("Watch the remaining", spectatorPayload.Instructions, StringComparison.Ordinal);
        Assert.Contains(simulated.Events, item =>
            item.Kind == "PlayerOverloaded" || item.Kind == "DisconnectForfeit");
    }

    [Fact]
    public void Action_decoder_rejects_malformed_inputs_and_preserves_optional_target()
    {
        var module = new PileUpPanicGameModule();
        var target = Guid.NewGuid();
        var decoded = Assert.IsType<SubmitPileInputAction>(module.DecodeAction(
            SubmitPileInputAction.ActionKind,
            JsonSerializer.SerializeToElement(new
            {
                sequence = 7,
                input = "ActivateAbility",
                targetPlayerId = target,
                clientTimestamp = "2026-09-03T12:00:00Z"
            })));

        Assert.Equal(7, decoded.Sequence);
        Assert.Equal(PileInputType.ActivateAbility, decoded.Input);
        Assert.Equal(target, decoded.TargetPlayerId);
        Assert.Throws<GameRuleViolationException>(() => module.DecodeAction(
            SubmitPileInputAction.ActionKind,
            JsonSerializer.SerializeToElement(new { sequence = -1, input = "wobble" })));
    }

    private static GameRuntimeManager Runtime(IGameModule module, IGameStateStore store) => new(
        new GameModuleCatalog([module]),
        store,
        TimeProvider.System);

    private static GameParticipant[] Participants(int count) => Enumerable.Range(1, count)
        .Select(index => new GameParticipant(
            Guid.Parse($"00000000-0000-0000-0000-{index:D12}"),
            $"Player {index}"))
        .ToArray();

    private static PileUpFlowOptions FastFlow() => new()
    {
        IntroductionDuration = TimeSpan.FromMilliseconds(25),
        ReadyDuration = TimeSpan.FromMilliseconds(25),
        ArenaRevealDuration = TimeSpan.FromMilliseconds(25),
        CountdownDuration = TimeSpan.FromMilliseconds(25),
        RoundResultDuration = TimeSpan.FromMilliseconds(25),
        StandingsDuration = TimeSpan.FromMilliseconds(25),
        FinalWinnerDuration = TimeSpan.FromMilliseconds(25),
        CelebrationDuration = TimeSpan.FromMilliseconds(25)
    };

    private static GameActionContext Context(
        GameInstanceId gameId,
        Guid partyId,
        GameActor actor,
        DateTimeOffset now) => new(gameId, partyId, actor, now);

    private static GameModuleState AdvanceDeadline(
        PileUpPanicGameModule module,
        GameModuleState state,
        GameInstanceId gameId,
        Guid partyId) => module.Apply(
            state,
            Context(gameId, partyId, GameActor.SystemActor, state.PhaseEndsAtUtc!.Value),
            new DeadlineElapsedAction(state.PhaseEndsAtUtc.Value)).State;

    private static async Task WaitForRevisionAsync(
        GameRuntimeManager runtime,
        GameInstanceId gameId,
        long revision)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            if ((await runtime.GetStatusAsync(gameId)).Revision >= revision)
            {
                return;
            }
            await Task.Delay(10);
        }
        throw new TimeoutException("The Pile-Up Panic simulation did not advance.");
    }

    private static async Task WaitForPhaseAsync(
        GameRuntimeManager runtime,
        GameInstanceId gameId,
        string phase)
    {
        for (var attempt = 0; attempt < 200; attempt++)
        {
            if ((await runtime.GetStatusAsync(gameId)).Phase == phase)
            {
                return;
            }
            await Task.Delay(10);
        }
        throw new TimeoutException($"Pile-Up Panic did not reach {phase}.");
    }

}
