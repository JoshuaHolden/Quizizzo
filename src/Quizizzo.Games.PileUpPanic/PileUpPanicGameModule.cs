using System.Text.Json;
using Quizizzo.GameContracts;

namespace Quizizzo.Games.PileUpPanic;

public sealed record SubmitPileInputAction(
    long Sequence,
    PileInputType Input,
    Guid? TargetPlayerId,
    DateTimeOffset ClientTimestamp) : IGameAction
{
    public const string ActionKind = "pile-up.input";
    public string Kind => ActionKind;
}

public sealed record AdvancePileRoundAction : IGameAction
{
    public const string ActionKind = "pile-up.advance";
    public string Kind => ActionKind;
}

public sealed record ReadyPileControllerAction : IGameAction
{
    public const string ActionKind = "pile-up.ready";
    public string Kind => ActionKind;
}

public sealed record PileUpFlowOptions
{
    public TimeSpan IntroductionDuration { get; init; } = TimeSpan.FromSeconds(5);
    public TimeSpan ReadyDuration { get; init; } = TimeSpan.FromSeconds(15);
    public TimeSpan ArenaRevealDuration { get; init; } = TimeSpan.FromSeconds(3);
    public TimeSpan CountdownDuration { get; init; } = TimeSpan.FromSeconds(3);
    public TimeSpan RoundResultDuration { get; init; } = TimeSpan.FromSeconds(4);
    public TimeSpan StandingsDuration { get; init; } = TimeSpan.FromSeconds(7);
    public TimeSpan FinalWinnerDuration { get; init; } = TimeSpan.FromSeconds(4);
    public TimeSpan CelebrationDuration { get; init; } = TimeSpan.FromSeconds(8);

    public void Validate()
    {
        foreach (var duration in new[]
        {
            IntroductionDuration,
            ReadyDuration,
            ArenaRevealDuration,
            CountdownDuration,
            RoundResultDuration,
            StandingsDuration,
            FinalWinnerDuration,
            CelebrationDuration
        })
        {
            if (duration <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(duration), "Pile-Up flow durations must be positive.");
            }
        }
    }
}

public sealed class PileUpPanicGameModule(
    PileUpOptions? matchOptions = null,
    TimeSpan? simulationInterval = null,
    PileUpFlowOptions? flowOptions = null) : IGameModule, IGameSimulationModule, IGamePlayerPresenceModule
{
    public const string GameKey = "pile-up-panic";
    public const string IntroductionPhase = "Introduction";
    public const string ControllerReadyPhase = "ControllerReady";
    public const string ArenaRevealPhase = "ArenaReveal";
    public const string CountdownPhase = "Countdown";
    public const string PlayingPhase = "Playing";
    public const string RoundResultPhase = "RoundResult";
    public const string StandingsPhase = "Standings";
    public const string FinalWinnerPhase = "FinalWinner";
    public const string WinnerCelebrationPhase = "WinnerCelebration";
    public const string CompletedPhase = "Completed";
    public const int MaximumRounds = 3;
    public const int WinsRequired = 2;

    private readonly PileUpOptions matchOptions = ValidateOptions(matchOptions);
    private readonly TimeSpan simulationInterval = ValidateDuration(
        simulationInterval ?? TimeSpan.FromMilliseconds(250),
        nameof(simulationInterval));
    private readonly PileUpFlowOptions flowOptions = ValidateFlowOptions(flowOptions);

    public GameDescriptor Descriptor { get; } = new(
        GameKey,
        "Pile-Up Panic",
        2,
        4,
        "Build circuits, weaponise chaos and survive the scrap pile across a best-of-three showdown.",
        "Realtime arcade · 2–4 players");

    public GameModuleState Start(GameStartContext context)
    {
        var participants = context.Participants
            .Select(participant => new PileMatchParticipant(participant.PlayerId, participant.DisplayName))
            .ToArray();
        var seed = SeedFrom(context.GameInstanceId.Value);
        var match = CreateMatch(context.GameInstanceId.Value, participants, seed, 1, context.StartedAtUtc);
        var state = new PileUpGameState(
            1,
            seed,
            participants,
            match.CaptureState(),
            participants.ToDictionary(participant => participant.PlayerId, _ => 0),
            participants.ToDictionary(participant => participant.PlayerId, _ => 0),
            participants.ToDictionary(participant => participant.PlayerId, _ => 0),
            new Dictionary<Guid, int>(),
            [],
            null,
            []);
        return ModuleState(
            IntroductionPhase,
            context.StartedAtUtc.Add(flowOptions.IntroductionDuration),
            false,
            state);
    }

    public GameTransition Apply(GameModuleState state, GameActionContext context, IGameAction action)
    {
        var game = ReadState(state);
        return action switch
        {
            SubmitPileInputAction input => ApplyInput(state, game, context, input),
            ReadyPileControllerAction => Ready(state, game, context),
            PlayerPresenceChangedAction presence => SetPlayerPresence(state, game, context, presence),
            SimulationTickElapsedAction => Simulate(state, game, context.ReceivedAtUtc),
            DeadlineElapsedAction => Progress(state, game, context.ReceivedAtUtc),
            AdvancePileRoundAction => Advance(state, game, context),
            _ => throw new GameRuleViolationException(
                "unsupported-action", $"Action '{action.Kind}' is not supported by Pile-Up Panic.")
        };
    }

    public GameViewPayload CreateView(GameModuleState state, GameViewContext context)
    {
        var game = ReadState(state);
        return context.Role switch
        {
            GameAudienceRole.Host => new(GameJson.From(HostView(state, game))),
            GameAudienceRole.Display => new(GameJson.From(DisplayView(state, game))),
            GameAudienceRole.Player => new(GameJson.From(PlayerView(state, game, context))),
            _ => throw new ArgumentOutOfRangeException(nameof(context))
        };
    }

    public IGameAction DecodeAction(string actionKind, JsonElement payload) => actionKind switch
    {
        SubmitPileInputAction.ActionKind => ReadInput(payload),
        ReadyPileControllerAction.ActionKind => new ReadyPileControllerAction(),
        AdvancePileRoundAction.ActionKind => new AdvancePileRoundAction(),
        _ => throw new GameRuleViolationException(
            "unsupported-action", $"Action '{actionKind}' is not supported by Pile-Up Panic.")
    };

    public TimeSpan? GetSimulationInterval(GameModuleState state) =>
        !state.IsComplete && state.Phase == PlayingPhase ? simulationInterval : null;

    private static GameTransition SetPlayerPresence(
        GameModuleState current,
        PileUpGameState game,
        GameActionContext context,
        PlayerPresenceChangedAction action)
    {
        if (context.Actor.Role != GameActorRole.System)
        {
            throw new GameRuleViolationException(
                "presence-forbidden", "Only the game runtime may update player presence.");
        }

        var match = PileUpMatch.Restore(game.Match);
        match.SetConnection(action.PlayerId, action.IsConnected, context.ReceivedAtUtc);
        var events = match.DrainEvents()
            .Select(item => new GameEvent(item.Kind, GameJson.From(item)))
            .ToArray();
        return new GameTransition(
            current with { Data = GameJson.From(game with { Match = match.CaptureState() }) },
            [],
            events);
    }

    private GameTransition Ready(
        GameModuleState current,
        PileUpGameState game,
        GameActionContext context)
    {
        RequirePhase(current, ControllerReadyPhase);
        if (!context.Actor.TryGetPlayerId(out var playerId) ||
            !game.Participants.Any(participant => participant.PlayerId == playerId))
        {
            throw new GameRuleViolationException("player-required", "A current player must ready this controller.");
        }
        if (game.ReadyPlayerIds.Contains(playerId))
        {
            throw new GameRuleViolationException("already-ready", "This controller is already ready.");
        }

        var ready = game.ReadyPlayerIds.Append(playerId).ToArray();
        var updated = game with { ReadyPlayerIds = ready };
        if (ready.Length == game.Participants.Count)
        {
            return EnterArenaReveal(updated, context.ReceivedAtUtc);
        }
        return new GameTransition(
            current with { Data = GameJson.From(updated) },
            [],
            [new GameEvent("PileControllerReady", GameJson.From(new { playerId }))]);
    }

    private GameTransition ApplyInput(
        GameModuleState current,
        PileUpGameState game,
        GameActionContext context,
        SubmitPileInputAction action)
    {
        RequirePhase(current, PlayingPhase);
        if (!context.Actor.TryGetPlayerId(out var playerId))
        {
            throw new GameRuleViolationException("player-required", "A current player must control this pile.");
        }

        var match = PileUpMatch.Restore(game.Match);
        var result = match.ApplyInput(
            playerId,
            new PileInputCommand(
                context.GameInstanceId.Value,
                action.Sequence,
                action.Input,
                action.TargetPlayerId,
                action.ClientTimestamp),
            context.ReceivedAtUtc);
        if (result.Kind == InputResultKind.Rejected)
        {
            throw new GameRuleViolationException(
                result.Code ?? "input-rejected",
                "That control input was rejected by the authoritative match.");
        }
        return TransitionAfterMatchAction(current, game, match, context.ReceivedAtUtc);
    }

    private GameTransition Simulate(
        GameModuleState current,
        PileUpGameState game,
        DateTimeOffset now)
    {
        RequirePhase(current, PlayingPhase);
        var match = PileUpMatch.Restore(game.Match);
        match.AdvanceSimulation(now);
        return TransitionAfterMatchAction(current, game, match, now);
    }

    private GameTransition TransitionAfterMatchAction(
        GameModuleState current,
        PileUpGameState game,
        PileUpMatch match,
        DateTimeOffset now)
    {
        var semanticEvents = match.DrainEvents()
            .Select(item => new GameEvent(item.Kind, GameJson.From(item)))
            .ToArray();
        if (!match.IsRoundComplete)
        {
            return new GameTransition(
                current with { Data = GameJson.From(game with { Match = match.CaptureState() }) },
                [],
                semanticEvents);
        }

        var snapshot = match.CreateSnapshot();
        var standings = Rank(snapshot);
        var wins = game.RoundWins.ToDictionary();
        var roundPoints = game.RoundPoints.ToDictionary();
        var performanceViews = game.PerformanceViews.ToDictionary();
        foreach (var standing in standings)
        {
            roundPoints[standing.PlayerId] = checked(
                roundPoints.GetValueOrDefault(standing.PlayerId) + standing.PlacementPoints);
            performanceViews[standing.PlayerId] = checked(
                performanceViews.GetValueOrDefault(standing.PlayerId) + standing.ArenaViews);
        }
        if (match.RoundWinnerId is { } winnerId)
        {
            wins[winnerId] = wins.GetValueOrDefault(winnerId) + 1;
        }
        var revealed = game with
        {
            Match = match.CaptureState(),
            RoundWins = wins,
            RoundPoints = roundPoints,
            PerformanceViews = performanceViews,
            Results = standings
        };
        var events = semanticEvents.Append(new GameEvent(
            "PileRoundResults",
            GameJson.From(new { game.RoundNumber, match.RoundWinnerId, standings }))).ToArray();
        return new GameTransition(
            ModuleState(RoundResultPhase, now.Add(flowOptions.RoundResultDuration), false, revealed),
            [],
            events);
    }

    private GameTransition Advance(
        GameModuleState current,
        PileUpGameState game,
        GameActionContext context)
    {
        if (context.Actor.Role != GameActorRole.Host)
        {
            throw new GameRuleViolationException("host-required", "Only the host can advance the standings.");
        }
        return Progress(current, game, context.ReceivedAtUtc);
    }

    private GameTransition Progress(GameModuleState current, PileUpGameState game, DateTimeOffset now)
    {
        return current.Phase switch
        {
            IntroductionPhase => EnterReady(game, now),
            ControllerReadyPhase => EnterArenaReveal(game, now),
            ArenaRevealPhase => EnterCountdown(game, now),
            CountdownPhase => EnterPlaying(game, now),
            PlayingPhase => Simulate(current, game, now),
            RoundResultPhase => EnterStandings(game, now),
            StandingsPhase => IsMatchDecided(game)
                ? EnterFinalWinner(game, now)
                : EnterNextRoundReveal(game, now),
            FinalWinnerPhase => new GameTransition(
                ModuleState(
                    WinnerCelebrationPhase,
                    now.Add(flowOptions.CelebrationDuration),
                    false,
                    game),
                [],
                [new GameEvent("PileWinnerCelebrationStarted", GameJson.From(new { game.MatchWinnerId }))]),
            WinnerCelebrationPhase => Complete(game),
            _ => throw new GameRuleViolationException("wrong-phase", "This phase cannot be advanced.")
        };
    }

    private GameTransition EnterReady(PileUpGameState game, DateTimeOffset now) => new(
        ModuleState(
            ControllerReadyPhase,
            now.Add(flowOptions.ReadyDuration),
            false,
            game with { ReadyPlayerIds = [] }),
        [],
        [new GameEvent("PileReadyCheckStarted", GameJson.Empty)]);

    private GameTransition EnterArenaReveal(PileUpGameState game, DateTimeOffset now) => new(
        ModuleState(
            ArenaRevealPhase,
            now.Add(flowOptions.ArenaRevealDuration),
            false,
            game),
        [],
        [new GameEvent("PileArenaRevealStarted", GameJson.From(new { game.RoundNumber }))]);

    private GameTransition EnterCountdown(PileUpGameState game, DateTimeOffset now) => new(
        ModuleState(
            CountdownPhase,
            now.Add(flowOptions.CountdownDuration),
            false,
            game),
        [],
        [new GameEvent("PileCountdownStarted", GameJson.From(new { game.RoundNumber }))]);

    private GameTransition EnterPlaying(PileUpGameState game, DateTimeOffset now)
    {
        var match = CreateMatch(game.Match.MatchId, game.Participants, game.Seed, game.RoundNumber, now);
        return new GameTransition(
            ModuleState(
                PlayingPhase,
                match.RoundEndsAtUtc,
                false,
                game with { Match = match.CaptureState(), Results = [] }),
            [],
            [new GameEvent("PileRoundStarted", GameJson.From(new { round = game.RoundNumber }))]);
    }

    private GameTransition EnterStandings(PileUpGameState game, DateTimeOffset now) => new(
        ModuleState(
            StandingsPhase,
            now.Add(flowOptions.StandingsDuration),
            false,
            game),
        [],
        [new GameEvent("PileStandingsStarted", GameJson.From(new { game.RoundNumber }))]);

    private GameTransition EnterNextRoundReveal(PileUpGameState game, DateTimeOffset now)
    {
        var nextRound = game.RoundNumber + 1;
        var preview = CreateMatch(game.Match.MatchId, game.Participants, game.Seed, nextRound, now);
        return EnterArenaReveal(game with
        {
            RoundNumber = nextRound,
            Match = preview.CaptureState(),
            Results = []
        }, now);
    }

    private GameTransition EnterFinalWinner(PileUpGameState game, DateTimeOffset now)
    {
        var winner = DetermineWinner(game);
        var finalViews = game.Participants.ToDictionary(
            participant => participant.PlayerId,
            participant => checked(
                game.PerformanceViews.GetValueOrDefault(participant.PlayerId) +
                (game.RoundPoints.GetValueOrDefault(participant.PlayerId) * 250) +
                (participant.PlayerId == winner ? 1000 : 0)));
        var final = game with { MatchWinnerId = winner, FinalViews = finalViews };
        return new GameTransition(
            ModuleState(
                FinalWinnerPhase,
                now.Add(flowOptions.FinalWinnerDuration),
                false,
                final),
            [],
            [new GameEvent("PileMatchWinnerRevealed", GameJson.From(new { winner, finalViews }))]);
    }

    private static GameTransition Complete(PileUpGameState game)
    {
        var awards = game.FinalViews
            .Where(pair => pair.Value > 0)
            .Select(pair => new ScoreAward(pair.Key, pair.Value, "Pile-Up Panic match performance"))
            .ToArray();
        return new GameTransition(
            ModuleState(CompletedPhase, null, true, game),
            awards,
            [new GameEvent("PileMatchCompleted", GameJson.From(new
            {
                winner = game.MatchWinnerId,
                game.RoundWins,
                game.FinalViews
            }))]);
    }

    private static HostGameViewPayload HostView(GameModuleState current, PileUpGameState game) => new(
        $"Pile-Up Panic · Round {game.RoundNumber}/{MaximumRounds}",
        "Complete circuits. Use chaos. Be the last pile standing.",
        PhaseMessage(current, game),
        ActivityCount(current, game),
        game.Match.Players.Count,
        CanHostAdvance(current),
        CanHostAdvance(current) ? AdvancePileRoundAction.ActionKind : null,
        CanHostAdvance(current) ? "Continue now" : null,
        PresentationEntries(current, game));

    private static DisplayGameViewPayload DisplayView(GameModuleState current, PileUpGameState game) => new(
        $"PILE-UP PANIC · ROUND {game.RoundNumber}/{MaximumRounds}",
        "Complete circuits. Cause chaos. Don't overload.",
        PhaseMessage(current, game),
        ActivityCount(current, game),
        game.Match.Players.Count,
        PresentationEntries(current, game),
        ShowRoundRanking: current.Phase is StandingsPhase or FinalWinnerPhase or WinnerCelebrationPhase,
        ScoreUnit: "pts",
        State: GameJson.From(new PileDisplayViewState(
            PileUpMatch.Restore(game.Match).CreateSnapshot(),
            game.RoundNumber,
            game.RoundWins,
            game.RoundPoints,
            game.PerformanceViews,
            game.FinalViews,
            game.Results,
            game.MatchWinnerId,
            game.ReadyPlayerIds,
            ScrapClusterCatalogue.All.ToDictionary(cluster => cluster.Key, cluster => cluster.Cells))));

    private PlayerGameViewPayload PlayerView(
        GameModuleState current,
        PileUpGameState game,
        GameViewContext context)
    {
        var playerId = context.PlayerId
            ?? throw new GameRuleViolationException("player-required", "A player view requires a player ID.");
        var own = game.Match.Players.SingleOrDefault(player => player.Arena.PlayerId == playerId)
            ?? throw new GameRuleViolationException("player-required", "The player is not in this match.");
        var opponents = game.Match.Players
            .Where(player => player.Arena.PlayerId != playerId)
            .Select(player => new PileOpponentView(
                player.Arena.PlayerId,
                player.Arena.DisplayName,
                player.Arena.Views,
                player.Arena.CircuitsCompleted,
                player.Arena.IsOverloaded,
                player.IsConnected))
            .ToArray();
        var ownResult = game.Results.SingleOrDefault(result => result.PlayerId == playerId);
        var instructions = PlayerInstructions(current, game, own, ownResult);
        return new PlayerGameViewPayload(
            $"Round {game.RoundNumber}/{MaximumRounds}",
            instructions,
            PlayerController(current, game, own, opponents),
            GameJson.From(new PilePlayerViewState(
                new PileOwnStatusView(
                    own.Arena.PlayerId,
                    own.Arena.DisplayName,
                    own.Arena.Active?.Material,
                    own.Arena.Views,
                    own.Arena.CircuitsCompleted,
                    own.Arena.ChaosCharge,
                    own.Arena.AvailableAbility,
                    own.Arena.Shielded,
                    own.Arena.IsOverloaded,
                    own.Arena.QueuedJunk),
                own.TargetPlayerId,
                own.IsConnected,
                own.LastSequence,
                opponents,
                game.RoundWins,
                game.RoundPoints,
                ownResult)),
            ScoreUnit: "pts");
    }

    private PlayerControllerView PlayerController(
        GameModuleState current,
        PileUpGameState game,
        PilePlayerRuntimeState own,
        IReadOnlyList<PileOpponentView> opponents)
    {
        if (current.Phase == ControllerReadyPhase && !game.ReadyPlayerIds.Contains(own.Arena.PlayerId))
        {
            return new PlayerControllerView(
                PlayerControllerKind.Choice,
                ReadyPileControllerAction.ActionKind,
                true,
                "Ready up",
                GameJson.From(new ChoiceControllerConfiguration(
                    [new ControllerOption("ready", "READY", "My controls are open")],
                    SelectionProperty: "ready",
                    SelectionScope: "pile-up-ready")));
        }
        if (current.Phase != PlayingPhase || own.Arena.IsOverloaded)
        {
            return WaitingController();
        }

        var targets = opponents
            .Where(opponent => !opponent.IsOverloaded)
            .Select(opponent => new ControllerOption(
                opponent.PlayerId.ToString("N"),
                opponent.DisplayName))
            .ToArray();
        return new PlayerControllerView(
            PlayerControllerKind.Arcade,
            SubmitPileInputAction.ActionKind,
            own.IsConnected,
            "Use ability",
            GameJson.From(new ArcadeControllerConfiguration(
                [
                    new("MoveLeft", "←", "Move left", (int)matchOptions.HorizontalRepeat.TotalMilliseconds),
                    new("MoveRight", "→", "Move right", (int)matchOptions.HorizontalRepeat.TotalMilliseconds),
                    new("RotateClockwise", "↻", "Rotate clockwise"),
                    new("SoftDrop", "↓", "Soft drop", (int)matchOptions.SoftDropRepeat.TotalMilliseconds),
                    new("InstantDrop", "⇊", "Instant drop"),
                    new("ActivateAbility", "⚡", "Activate chaos ability")
                ],
                own.LastSequence + 1,
                targets,
                own.TargetPlayerId?.ToString("N"),
                own.Arena.AvailableAbility?.ToString(),
                own.Arena.ChaosCharge,
                new ArcadeArenaConfiguration(
                    PileUpOptions.Columns,
                    PileUpOptions.VisibleRows,
                    PileUpOptions.HiddenRows,
                    own.Arena.Grid.Select(cell =>
                        new ArcadeArenaCell(cell.X, cell.Y, cell.Material)).ToArray(),
                    own.Arena.Active is { } active
                        ? new ArcadeActivePiece(
                            active.ClusterKey,
                            active.Material,
                            active.X,
                            active.Y,
                            active.Rotation)
                        : null,
                    own.Arena.Upcoming.Select(item =>
                        new ArcadeUpcomingPiece(item.ClusterKey, item.Material)).ToArray(),
                    ScrapClusterCatalogue.All.ToDictionary(
                        cluster => cluster.Key,
                        cluster => (IReadOnlyList<ArcadeGridPoint>)cluster.Cells
                            .Select(cell => new ArcadeGridPoint(cell.X, cell.Y))
                            .ToArray())))));
    }

    private static string PlayerInstructions(
        GameModuleState current,
        PileUpGameState game,
        PilePlayerRuntimeState own,
        PileRoundStanding? ownResult) => current.Phase switch
        {
            IntroductionPhase => "Get ready to complete circuits and unleash chaos.",
            ControllerReadyPhase when game.ReadyPlayerIds.Contains(own.Arena.PlayerId) =>
                "Ready. Waiting for the other controllers…",
            ControllerReadyPhase => "Tap ready when you can see your controls.",
            ArenaRevealPhase => "Your scrapyard is loading onto the main screen.",
            CountdownPhase => "Hands ready. The round is about to start.",
            PlayingPhase when own.Arena.IsOverloaded =>
                "Your pile overloaded. Watch the remaining scrapyards battle it out.",
            PlayingPhase => "Your arena is live.",
            RoundResultPhase or StandingsPhase when ownResult is not null =>
                $"You placed #{ownResult.Rank} and earned {ownResult.PlacementPoints} round points.",
            FinalWinnerPhase or WinnerCelebrationPhase when game.MatchWinnerId == own.Arena.PlayerId =>
                "You won Pile-Up Panic!",
            FinalWinnerPhase or WinnerCelebrationPhase => "The final winner is celebrating.",
            _ => "Pile-Up Panic complete."
        };

    private static PlayerControllerView WaitingController() => new(
        PlayerControllerKind.Waiting,
        string.Empty,
        false,
        string.Empty,
        GameJson.Empty);

    private static GamePresentationEntry[] PresentationEntries(
        GameModuleState current,
        PileUpGameState game)
    {
        if (current.Phase is RoundResultPhase or StandingsPhase or FinalWinnerPhase or WinnerCelebrationPhase ||
            current.IsComplete)
        {
            return game.Results.Select(result => new GamePresentationEntry(
                result.PlayerId,
                result.DisplayName,
                current.IsComplete
                    ? $"{game.FinalViews.GetValueOrDefault(result.PlayerId):N0} views · " +
                      $"{game.RoundWins.GetValueOrDefault(result.PlayerId)} wins"
                    : $"+{result.PlacementPoints} round pts · {result.CircuitsCompleted} circuits",
                result.Rank,
                current.IsComplete ? game.FinalViews.GetValueOrDefault(result.PlayerId) : 0)).ToArray();
        }
        return game.Match.Players.Select(player => new GamePresentationEntry(
            player.Arena.PlayerId,
            player.Arena.DisplayName,
            player.Arena.IsOverloaded
                ? "OVERLOADED"
                : $"{player.Arena.Views:N0} views · {player.Arena.CircuitsCompleted} circuits",
            null,
            0)).ToArray();
    }

    private static string PhaseMessage(GameModuleState current, PileUpGameState game) => current.Phase switch
    {
        IntroductionPhase => "Welcome to the scrapyard",
        ControllerReadyPhase => $"{game.ReadyPlayerIds.Count}/{game.Participants.Count} controllers ready",
        ArenaRevealPhase => "Arena systems online",
        CountdownPhase => "Round starts in…",
        PlayingPhase => $"{game.Match.Players.Count(player => !player.Arena.IsOverloaded)} piles still operational",
        RoundResultPhase => $"Round {game.RoundNumber} complete",
        StandingsPhase => $"Standings after round {game.RoundNumber}",
        FinalWinnerPhase => "Final survivor revealed",
        WinnerCelebrationPhase => "Winner celebration",
        _ => "Pile-Up Panic complete"
    };

    private static int ActivityCount(GameModuleState current, PileUpGameState game) =>
        current.Phase == ControllerReadyPhase
            ? game.ReadyPlayerIds.Count
            : game.Match.Players.Count(player => player.Arena.IsOverloaded);

    private static bool CanHostAdvance(GameModuleState current) =>
        !current.IsComplete && current.Phase != PlayingPhase;

    private static PileRoundStanding[] Rank(PileMatchSnapshot snapshot) => snapshot.Arenas
        .OrderBy(arena => arena.IsOverloaded)
        .ThenBy(arena => StackHeight(arena.Grid))
        .ThenByDescending(arena => arena.CircuitsCompleted)
        .ThenByDescending(arena => arena.Views)
        .ThenBy(arena => arena.PlayerId)
        .Select((arena, index) => new PileRoundStanding(
            arena.PlayerId,
            arena.DisplayName,
            index + 1,
            arena.Views,
            arena.CircuitsCompleted,
            PlacementPoints(index + 1)))
        .ToArray();

    private static int PlacementPoints(int rank) => rank switch
    {
        1 => 4,
        2 => 2,
        3 => 1,
        _ => 0
    };

    private static int StackHeight(IReadOnlyList<ArenaCell> cells) => cells.Count == 0
        ? 0
        : PileUpOptions.TotalRows - cells.Min(cell => cell.Y);

    private static bool IsMatchDecided(PileUpGameState game) =>
        game.RoundNumber >= MaximumRounds || game.RoundWins.Values.Any(wins => wins >= WinsRequired);

    private static Guid DetermineWinner(PileUpGameState game) => game.RoundWins
        .OrderByDescending(pair => pair.Value)
        .ThenByDescending(pair => game.RoundPoints.GetValueOrDefault(pair.Key))
        .ThenByDescending(pair => game.PerformanceViews.GetValueOrDefault(pair.Key))
        .ThenBy(pair => pair.Key)
        .First().Key;

    private PileUpMatch CreateMatch(
        Guid matchId,
        IReadOnlyList<PileMatchParticipant> participants,
        ulong seed,
        int roundNumber,
        DateTimeOffset now) => new(
            matchId,
            participants,
            seed ^ (0x9E3779B97F4A7C15UL * (ulong)roundNumber),
            now,
            matchOptions);

    private static SubmitPileInputAction ReadInput(JsonElement payload)
    {
        if (payload.ValueKind != JsonValueKind.Object ||
            !payload.TryGetProperty("sequence", out var sequenceElement) ||
            !sequenceElement.TryGetInt64(out var sequence) || sequence < 0 ||
            !payload.TryGetProperty("input", out var inputElement) ||
            inputElement.ValueKind != JsonValueKind.String ||
            !Enum.TryParse<PileInputType>(inputElement.GetString(), true, out var input) ||
            !Enum.IsDefined(input))
        {
            throw new GameRuleViolationException("invalid-pile-input", "A valid sequenced pile control is required.");
        }
        Guid? target = null;
        if (payload.TryGetProperty("targetPlayerId", out var targetElement) &&
            targetElement.ValueKind is not JsonValueKind.Null)
        {
            if (targetElement.ValueKind != JsonValueKind.String ||
                !Guid.TryParse(targetElement.GetString(), out var parsedTarget))
            {
                throw new GameRuleViolationException("invalid-pile-target", "The selected target is invalid.");
            }
            target = parsedTarget;
        }
        var clientTimestamp = DateTimeOffset.UnixEpoch;
        if (payload.TryGetProperty("clientTimestamp", out var timestampElement) &&
            (timestampElement.ValueKind != JsonValueKind.String ||
             !timestampElement.TryGetDateTimeOffset(out clientTimestamp)))
        {
            throw new GameRuleViolationException("invalid-client-time", "The diagnostic client timestamp is invalid.");
        }
        return new SubmitPileInputAction(sequence, input, target, clientTimestamp);
    }

    private static void RequirePhase(GameModuleState current, string expected)
    {
        if (current.Phase != expected)
        {
            throw new GameRuleViolationException("wrong-phase", "That action is not available in this phase.");
        }
    }

    private static PileUpGameState ReadState(GameModuleState state) =>
        state.Data.Deserialize<PileUpGameState>()
        ?? throw new InvalidOperationException("Pile-Up Panic state could not be read.");

    private static GameModuleState ModuleState(
        string phase,
        DateTimeOffset? deadline,
        bool complete,
        PileUpGameState state) => new(1, phase, deadline, complete, GameJson.From(state));

    private static PileUpOptions ValidateOptions(PileUpOptions? options)
    {
        var value = options ?? new PileUpOptions();
        value.Validate();
        return value;
    }

    private static PileUpFlowOptions ValidateFlowOptions(PileUpFlowOptions? options)
    {
        var value = options ?? new PileUpFlowOptions();
        value.Validate();
        return value;
    }

    private static TimeSpan ValidateDuration(TimeSpan value, string parameterName)
    {
        if (value <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(parameterName, "The duration must be positive.");
        }
        return value;
    }

    private static ulong SeedFrom(Guid value)
    {
        var seed = 1469598103934665603UL;
        foreach (var item in value.ToByteArray())
        {
            seed = (seed ^ item) * 1099511628211UL;
        }
        return seed;
    }
}

public sealed record PileUpGameState(
    int RoundNumber,
    ulong Seed,
    IReadOnlyList<PileMatchParticipant> Participants,
    PileUpMatchState Match,
    IReadOnlyDictionary<Guid, int> RoundWins,
    IReadOnlyDictionary<Guid, int> RoundPoints,
    IReadOnlyDictionary<Guid, int> PerformanceViews,
    IReadOnlyDictionary<Guid, int> FinalViews,
    IReadOnlyList<Guid> ReadyPlayerIds,
    Guid? MatchWinnerId,
    IReadOnlyList<PileRoundStanding> Results);

public sealed record PileRoundStanding(
    Guid PlayerId,
    string DisplayName,
    int Rank,
    int ArenaViews,
    int CircuitsCompleted,
    int PlacementPoints);

public sealed record PileOpponentView(
    Guid PlayerId,
    string DisplayName,
    int Views,
    int CircuitsCompleted,
    bool IsOverloaded,
    bool IsConnected);

public sealed record PileOwnStatusView(
    Guid PlayerId,
    string DisplayName,
    string? Material,
    int Views,
    int CircuitsCompleted,
    int ChaosCharge,
    ChaosAbility? AvailableAbility,
    bool Shielded,
    bool IsOverloaded,
    int QueuedJunk);

public sealed record PilePlayerViewState(
    PileOwnStatusView Arena,
    Guid? TargetPlayerId,
    bool IsConnected,
    long LastSequence,
    IReadOnlyList<PileOpponentView> Opponents,
    IReadOnlyDictionary<Guid, int> RoundWins,
    IReadOnlyDictionary<Guid, int> RoundPoints,
    PileRoundStanding? Result);

public sealed record PileDisplayViewState(
    PileMatchSnapshot Match,
    int RoundNumber,
    IReadOnlyDictionary<Guid, int> RoundWins,
    IReadOnlyDictionary<Guid, int> RoundPoints,
    IReadOnlyDictionary<Guid, int> PerformanceViews,
    IReadOnlyDictionary<Guid, int> FinalViews,
    IReadOnlyList<PileRoundStanding> Results,
    Guid? MatchWinnerId,
    IReadOnlyList<Guid> ReadyPlayerIds,
    IReadOnlyDictionary<string, IReadOnlyList<GridPoint>> ClusterShapes);
