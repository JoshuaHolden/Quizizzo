namespace Quizizzo.Games.PileUpPanic;

public enum PileInputType
{
    MoveLeft,
    MoveRight,
    RotateClockwise,
    SoftDrop,
    InstantDrop,
    ActivateAbility,
    SelectTarget
}

public enum InputResultKind
{
    Applied,
    Ignored,
    Rejected
}

public sealed record PileInputCommand(
    Guid MatchId,
    long Sequence,
    PileInputType Type,
    Guid? TargetPlayerId,
    DateTimeOffset ClientTimestamp);

public sealed record PileInputResult(InputResultKind Kind, string? Code = null);

public sealed record PileMatchParticipant(Guid PlayerId, string DisplayName);

public sealed record PileMatchEvent(string Kind, Guid? PlayerId = null, Guid? TargetPlayerId = null, int Value = 0);

public sealed class PileUpMatch
{
    private readonly PileUpOptions options;
    private readonly Dictionary<Guid, PlayerRuntime> runtimes;
    private readonly List<PileMatchEvent> events = [];
    private DateTimeOffset lastSimulationAt;

    public PileUpMatch(
        Guid matchId,
        IReadOnlyList<PileMatchParticipant> participants,
        ulong seed,
        DateTimeOffset startedAtUtc,
        PileUpOptions? options = null)
    {
        if (participants.Count is < 2 or > 4)
        {
            throw new ArgumentOutOfRangeException(nameof(participants), "Pile-Up Panic requires two to four players.");
        }
        if (participants.Select(player => player.PlayerId).Distinct().Count() != participants.Count)
        {
            throw new ArgumentException("Participant IDs must be unique.", nameof(participants));
        }

        this.options = options ?? new PileUpOptions();
        this.options.Validate();
        MatchId = matchId;
        StartedAtUtc = startedAtUtc;
        RoundEndsAtUtc = startedAtUtc.Add(this.options.RoundDuration);
        lastSimulationAt = startedAtUtc;
        runtimes = participants.Select((participant, index) => new PlayerRuntime(
            new PileArena(participant.PlayerId, participant.DisplayName, DeriveSeed(seed, index)),
            startedAtUtc)).ToDictionary(item => item.Arena.PlayerId);
        foreach (var runtime in runtimes.Values)
        {
            runtime.TargetPlayerId = NextTarget(runtime.Arena.PlayerId);
        }
        events.Add(new PileMatchEvent("MatchStarted"));
        events.Add(new PileMatchEvent("RoundStarted", Value: 1));
    }

    private PileUpMatch(PileUpMatchState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        state.Options.Validate();
        if (state.MatchId == Guid.Empty || state.Players.Count is < 2 or > 4 ||
            state.Players.Select(player => player.Arena.PlayerId).Distinct().Count() != state.Players.Count ||
            state.LastSimulationAtUtc < state.StartedAtUtc || state.RoundEndsAtUtc <= state.StartedAtUtc ||
            state.RoundWinnerId is { } winner && !state.Players.Any(player => player.Arena.PlayerId == winner))
        {
            throw new InvalidDataException("The restored Pile-Up Panic match state is invalid.");
        }
        options = state.Options;
        MatchId = state.MatchId;
        StartedAtUtc = state.StartedAtUtc;
        RoundEndsAtUtc = state.RoundEndsAtUtc;
        lastSimulationAt = state.LastSimulationAtUtc;
        IsRoundComplete = state.IsRoundComplete;
        RoundWinnerId = state.RoundWinnerId;
        runtimes = state.Players.Select(player => new PlayerRuntime(
            new PileArena(player.Arena),
            player.LastFallAtUtc)
        {
            LastSequence = player.LastSequence,
            TargetPlayerId = player.TargetPlayerId,
            IsConnected = player.IsConnected,
            DisconnectedAtUtc = player.DisconnectedAtUtc,
            LastFallAtUtc = player.LastFallAtUtc,
            AbilityReadyAtUtc = player.AbilityReadyAtUtc,
            GroundedSinceAtUtc = player.GroundedSinceAtUtc
        }).ToDictionary(runtime => runtime.Arena.PlayerId);
        foreach (var player in state.Players)
        {
            var runtime = runtimes[player.Arena.PlayerId];
            if (player.LastSequence < -1 || player.LastFallAtUtc < state.StartedAtUtc ||
                player.AbilityReadyAtUtc < state.StartedAtUtc ||
                player.GroundedSinceAtUtc is { } grounded && grounded < state.StartedAtUtc ||
                player.InputTimes.Any(timestamp => timestamp < state.StartedAtUtc) ||
                player.JunkReceivedTimes.Any(timestamp => timestamp < state.StartedAtUtc) ||
                player.IsConnected == (player.DisconnectedAtUtc is not null))
            {
                throw new InvalidDataException("A restored player runtime is invalid.");
            }
            foreach (var timestamp in player.InputTimes)
            {
                runtime.InputTimes.Enqueue(timestamp);
            }
            foreach (var timestamp in player.JunkReceivedTimes)
            {
                runtime.JunkReceivedTimes.Enqueue(timestamp);
            }
            if (runtime.TargetPlayerId is { } target && !runtimes.ContainsKey(target))
            {
                throw new InvalidDataException("A restored target is not in the match.");
            }
        }
    }

    public Guid MatchId { get; }
    public DateTimeOffset StartedAtUtc { get; }
    public DateTimeOffset RoundEndsAtUtc { get; }
    public bool IsRoundComplete { get; private set; }
    public Guid? RoundWinnerId { get; private set; }
    public IReadOnlyDictionary<Guid, PileArena> Arenas => runtimes.ToDictionary(pair => pair.Key, pair => pair.Value.Arena);
    public IReadOnlyList<PileMatchEvent> Events => events;

    public static PileUpMatch Restore(PileUpMatchState state) => new(state);

    public PileInputResult ApplyInput(Guid authenticatedPlayerId, PileInputCommand command, DateTimeOffset receivedAtUtc)
    {
        if (command.MatchId != MatchId)
        {
            return Reject("wrong-match", authenticatedPlayerId);
        }
        if (IsRoundComplete || !runtimes.TryGetValue(authenticatedPlayerId, out var runtime) || runtime.Arena.IsOverloaded)
        {
            return Reject("player-inactive", authenticatedPlayerId);
        }
        if (!runtime.IsConnected)
        {
            return Reject("player-disconnected", authenticatedPlayerId);
        }
        if (command.Sequence <= runtime.LastSequence)
        {
            return Reject(command.Sequence == runtime.LastSequence ? "duplicate-input" : "stale-input", authenticatedPlayerId);
        }
        runtime.LastSequence = command.Sequence;
        while (runtime.InputTimes.Count > 0 && receivedAtUtc - runtime.InputTimes.Peek() >= TimeSpan.FromSeconds(1))
        {
            runtime.InputTimes.Dequeue();
        }
        if (runtime.InputTimes.Count >= options.InputLimitPerSecond)
        {
            return Reject("input-rate-exceeded", authenticatedPlayerId);
        }
        runtime.InputTimes.Enqueue(receivedAtUtc);

        var applied = command.Type switch
        {
            PileInputType.MoveLeft => MoveHorizontal(runtime, -1, receivedAtUtc),
            PileInputType.MoveRight => MoveHorizontal(runtime, 1, receivedAtUtc),
            PileInputType.RotateClockwise => Rotate(runtime, receivedAtUtc),
            PileInputType.SoftDrop => SoftDrop(runtime, receivedAtUtc),
            PileInputType.InstantDrop => InstantDrop(runtime, receivedAtUtc),
            PileInputType.SelectTarget => SelectTarget(runtime, command.TargetPlayerId),
            PileInputType.ActivateAbility => ActivateAbility(runtime, command.TargetPlayerId, receivedAtUtc),
            _ => false
        };
        if (applied && command.Type is not (PileInputType.ActivateAbility or PileInputType.SelectTarget))
        {
            var eventKind = command.Type switch
            {
                PileInputType.RotateClockwise => "ClusterRotated",
                PileInputType.InstantDrop => "ClusterDropped",
                _ => "ClusterMoved"
            };
            events.Add(new PileMatchEvent(eventKind, authenticatedPlayerId));
        }
        return new PileInputResult(applied ? InputResultKind.Applied : InputResultKind.Ignored);
    }

    public void AdvanceSimulation(DateTimeOffset now)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(now, lastSimulationAt);
        if (IsRoundComplete)
        {
            return;
        }

        var elapsed = now - lastSimulationAt;
        var steps = Math.Min(5000, (int)(elapsed.Ticks / options.SimulationStep.Ticks));
        for (var step = 0; step < steps && !IsRoundComplete; step++)
        {
            lastSimulationAt = lastSimulationAt.Add(options.SimulationStep);
            SimulateStep(lastSimulationAt);
        }
        if (now >= RoundEndsAtUtc && !IsRoundComplete)
        {
            EndRoundByRanking();
        }
    }

    public void SetConnection(Guid playerId, bool connected, DateTimeOffset atUtc)
    {
        if (!runtimes.TryGetValue(playerId, out var runtime))
        {
            throw new ArgumentException("The player is not in this match.", nameof(playerId));
        }
        if (runtime.IsConnected == connected)
        {
            return;
        }
        runtime.IsConnected = connected;
        runtime.DisconnectedAtUtc = connected ? null : atUtc;
        events.Add(new PileMatchEvent(connected ? "PlayerReconnected" : "PlayerDisconnected", playerId));
    }

    public PileMatchSnapshot CreateSnapshot()
    {
        var active = runtimes.Values.Count(runtime => !runtime.Arena.IsOverloaded);
        return new PileMatchSnapshot(
            MatchId,
            StartedAtUtc,
            RoundEndsAtUtc,
            IsRoundComplete,
            RoundWinnerId,
            runtimes.Values.Select(runtime => new PileArenaSnapshot(
                runtime.Arena.PlayerId,
                runtime.Arena.DisplayName,
                runtime.Arena.Grid.OccupiedCells(),
                runtime.Arena.Active,
                runtime.Arena.Upcoming,
                runtime.Arena.Views,
                runtime.Arena.CircuitsCompleted,
                runtime.Arena.ChaosCharge,
                runtime.Arena.AvailableAbility,
                runtime.Arena.Shielded,
                runtime.Arena.IsOverloaded,
                runtime.Arena.QueuedJunk,
                runtime.TargetPlayerId,
                runtime.IsConnected,
                runtime.LastSequence)).ToArray(),
            LayoutFor(runtimes.Count),
            active);
    }

    public PileUpMatchState CaptureState() => new(
        MatchId,
        StartedAtUtc,
        RoundEndsAtUtc,
        lastSimulationAt,
        IsRoundComplete,
        RoundWinnerId,
        options,
        runtimes.Values.Select(runtime => new PilePlayerRuntimeState(
            runtime.Arena.CaptureState(),
            runtime.LastSequence,
            runtime.InputTimes.ToArray(),
            runtime.JunkReceivedTimes.ToArray(),
            runtime.TargetPlayerId,
            runtime.IsConnected,
            runtime.DisconnectedAtUtc,
            runtime.LastFallAtUtc,
            runtime.AbilityReadyAtUtc,
            runtime.GroundedSinceAtUtc)).ToArray());

    public IReadOnlyList<PileMatchEvent> DrainEvents()
    {
        var drained = events.ToArray();
        events.Clear();
        return drained;
    }

    public static string LayoutFor(int playerCount) => playerCount switch
    {
        2 => "side-by-side",
        3 => "one-over-two",
        4 => "two-by-two",
        _ => throw new ArgumentOutOfRangeException(nameof(playerCount))
    };

    private void SimulateStep(DateTimeOffset now)
    {
        foreach (var runtime in runtimes.Values.Where(item => !item.Arena.IsOverloaded))
        {
            if (!runtime.IsConnected &&
                runtime.DisconnectedAtUtc is { } disconnectedAt &&
                now - disconnectedAt >= options.DisconnectGracePeriod)
            {
                Overload(runtime, "DisconnectForfeit");
                continue;
            }

            var interval = CurrentFallInterval(now);
            if (runtime.GroundedSinceAtUtc is { } groundedAt)
            {
                if (runtime.Arena.CanMove(0, 1))
                {
                    runtime.GroundedSinceAtUtc = null;
                }
                else if (now - groundedAt >= options.LockDelay)
                {
                    Lock(runtime, now);
                    continue;
                }
            }
            if (now - runtime.LastFallAtUtc < interval)
            {
                continue;
            }
            runtime.LastFallAtUtc = now;
            if (!runtime.Arena.TryMove(0, 1))
            {
                runtime.GroundedSinceAtUtc ??= now;
            }
            else
            {
                runtime.GroundedSinceAtUtc = null;
            }
        }
        CheckLastOperational();
    }

    private static bool MoveHorizontal(PlayerRuntime runtime, int delta, DateTimeOffset now)
    {
        if (!runtime.Arena.TryMove(delta, 0))
        {
            return false;
        }
        PreserveOrClearGrounded(runtime, now);
        return true;
    }

    private static bool Rotate(PlayerRuntime runtime, DateTimeOffset now)
    {
        if (!runtime.Arena.TryRotateClockwise())
        {
            return false;
        }
        PreserveOrClearGrounded(runtime, now);
        return true;
    }

    private static bool SoftDrop(PlayerRuntime runtime, DateTimeOffset now)
    {
        if (runtime.Arena.TryMove(0, 1))
        {
            runtime.Arena.AddSoftDropView();
            runtime.GroundedSinceAtUtc = null;
            return true;
        }
        runtime.GroundedSinceAtUtc ??= now;
        return false;
    }

    private bool InstantDrop(PlayerRuntime runtime, DateTimeOffset now)
    {
        var distance = runtime.Arena.InstantDrop();
        runtime.Arena.AddInstantDropViews(distance);
        Lock(runtime, now);
        return true;
    }

    private void Lock(PlayerRuntime runtime, DateTimeOffset now)
    {
        var result = runtime.Arena.LockActive();
        events.Add(new PileMatchEvent("ClusterLocked", runtime.Arena.PlayerId));
        if (result.CircuitsCompleted > 0)
        {
            events.Add(new PileMatchEvent("CircuitCompleted", runtime.Arena.PlayerId, Value: result.CircuitsCompleted));
        }
        if (result.AbilityEarned)
        {
            events.Add(new PileMatchEvent("AbilityEarned", runtime.Arena.PlayerId));
        }
        if (result.JunkApplied > 0)
        {
            events.Add(new PileMatchEvent("JunkApplied", runtime.Arena.PlayerId, Value: result.JunkApplied));
        }
        if (result.Overloaded)
        {
            Overload(runtime, "PlayerOverloaded");
        }
        runtime.LastFallAtUtc = now;
        runtime.GroundedSinceAtUtc = null;
        CheckLastOperational();
    }

    private bool SelectTarget(PlayerRuntime runtime, Guid? requested)
    {
        if (requested is not { } target || !IsValidOffensiveTarget(runtime.Arena.PlayerId, target))
        {
            return false;
        }
        runtime.TargetPlayerId = target;
        return true;
    }

    private bool ActivateAbility(PlayerRuntime runtime, Guid? requestedTarget, DateTimeOffset now)
    {
        if (runtime.Arena.AvailableAbility is not { } ability || now < runtime.AbilityReadyAtUtc)
        {
            return false;
        }
        if (ability == ChaosAbility.Shield)
        {
            runtime.Arena.TakeAbility();
            runtime.Arena.ActivateShield();
            runtime.AbilityReadyAtUtc = now.Add(options.AbilityCooldown);
            events.Add(new PileMatchEvent("AbilityUsed", runtime.Arena.PlayerId));
            return true;
        }

        var targetId = requestedTarget ?? runtime.TargetPlayerId;
        if (targetId is not { } validTarget || !IsValidOffensiveTarget(runtime.Arena.PlayerId, validTarget))
        {
            return false;
        }
        var targetRuntime = runtimes[validTarget];
        var target = targetRuntime.Arena;
        runtime.Arena.TakeAbility();
        runtime.AbilityReadyAtUtc = now.Add(options.AbilityCooldown);
        if (target.ConsumeShield())
        {
            events.Add(new PileMatchEvent("AbilityBlocked", runtime.Arena.PlayerId, validTarget));
            return true;
        }
        if (ability == ChaosAbility.SendJunk)
        {
            TrimJunkWindow(targetRuntime, now);
            if (targetRuntime.JunkReceivedTimes.Count < options.MaximumJunkPerWindow)
            {
                var queued = target.QueueJunk(1, options.MaximumQueuedJunk);
                if (queued > 0)
                {
                    targetRuntime.JunkReceivedTimes.Enqueue(now);
                    events.Add(new PileMatchEvent("IncomingJunk", runtime.Arena.PlayerId, validTarget, queued));
                }
            }
        }
        else
        {
            target.ScrambleUpcoming();
        }
        events.Add(new PileMatchEvent("AbilityUsed", runtime.Arena.PlayerId, validTarget));
        return true;
    }

    private bool IsValidOffensiveTarget(Guid source, Guid target) =>
        source != target && runtimes.TryGetValue(target, out var runtime) && !runtime.Arena.IsOverloaded;

    private Guid? NextTarget(Guid source)
    {
        var ids = runtimes.Keys.OrderBy(id => id).ToArray();
        var start = Array.IndexOf(ids, source);
        foreach (var offset in Enumerable.Range(1, ids.Length - 1))
        {
            var candidate = ids[(start + offset) % ids.Length];
            if (IsValidOffensiveTarget(source, candidate))
            {
                return candidate;
            }
        }
        return null;
    }

    private void Overload(PlayerRuntime runtime, string eventKind)
    {
        runtime.Arena.ForceOverload();
        events.Add(new PileMatchEvent(eventKind, runtime.Arena.PlayerId));
        foreach (var other in runtimes.Values.Where(item => item.TargetPlayerId == runtime.Arena.PlayerId))
        {
            other.TargetPlayerId = NextTarget(other.Arena.PlayerId);
        }
    }

    private void CheckLastOperational()
    {
        var operational = runtimes.Values.Where(runtime => !runtime.Arena.IsOverloaded).ToArray();
        if (operational.Length != 1 || IsRoundComplete)
        {
            return;
        }
        IsRoundComplete = true;
        RoundWinnerId = operational[0].Arena.PlayerId;
        events.Add(new PileMatchEvent("RoundCompleted", RoundWinnerId));
    }

    private void EndRoundByRanking()
    {
        IsRoundComplete = true;
        RoundWinnerId = runtimes.Values
            .OrderBy(runtime => runtime.Arena.IsOverloaded)
            .ThenBy(runtime => runtime.Arena.Grid.StackHeight())
            .ThenByDescending(runtime => runtime.Arena.CircuitsCompleted)
            .ThenByDescending(runtime => runtime.Arena.Views)
            .ThenBy(runtime => runtime.Arena.PlayerId)
            .First().Arena.PlayerId;
        events.Add(new PileMatchEvent("RoundCompleted", RoundWinnerId));
    }

    private TimeSpan CurrentFallInterval(DateTimeOffset now)
    {
        var progressions = (int)((now - StartedAtUtc).Ticks / options.SpeedUpEvery.Ticks);
        var interval = options.InitialFallInterval - (options.SpeedUpBy * progressions);
        return interval < options.MinimumFallInterval ? options.MinimumFallInterval : interval;
    }

    private void TrimJunkWindow(PlayerRuntime runtime, DateTimeOffset now)
    {
        while (runtime.JunkReceivedTimes.Count > 0 && now - runtime.JunkReceivedTimes.Peek() >= options.JunkWindow)
        {
            runtime.JunkReceivedTimes.Dequeue();
        }
    }

    private static void PreserveOrClearGrounded(PlayerRuntime runtime, DateTimeOffset now)
    {
        runtime.GroundedSinceAtUtc = runtime.Arena.CanMove(0, 1)
            ? null
            : runtime.GroundedSinceAtUtc ?? now;
    }

    private PileInputResult Reject(string code, Guid playerId)
    {
        events.Add(new PileMatchEvent("InputRejected", playerId));
        return new PileInputResult(InputResultKind.Rejected, code);
    }

    private static ulong DeriveSeed(ulong seed, int playerIndex)
    {
        var mixed = seed ^ (0x9E3779B97F4A7C15UL * (ulong)(playerIndex + 1));
        return mixed == 0 ? (ulong)(playerIndex + 1) : mixed;
    }

    private sealed class PlayerRuntime(PileArena arena, DateTimeOffset startedAtUtc)
    {
        public PileArena Arena { get; } = arena;
        public long LastSequence { get; set; } = -1;
        public Queue<DateTimeOffset> InputTimes { get; } = new();
        public Queue<DateTimeOffset> JunkReceivedTimes { get; } = new();
        public Guid? TargetPlayerId { get; set; }
        public bool IsConnected { get; set; } = true;
        public DateTimeOffset? DisconnectedAtUtc { get; set; }
        public DateTimeOffset LastFallAtUtc { get; set; } = startedAtUtc;
        public DateTimeOffset AbilityReadyAtUtc { get; set; } = startedAtUtc;
        public DateTimeOffset? GroundedSinceAtUtc { get; set; }
    }
}

public sealed record PileMatchSnapshot(
    Guid MatchId,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset RoundEndsAtUtc,
    bool IsRoundComplete,
    Guid? RoundWinnerId,
    IReadOnlyList<PileArenaSnapshot> Arenas,
    string Layout,
    int OperationalPlayers);

public sealed record PileArenaSnapshot(
    Guid PlayerId,
    string DisplayName,
    IReadOnlyList<ArenaCell> Grid,
    ActiveScrap? Active,
    IReadOnlyList<GeneratedScrap> Upcoming,
    int Views,
    int CircuitsCompleted,
    int ChaosCharge,
    ChaosAbility? AvailableAbility,
    bool Shielded,
    bool IsOverloaded,
    int QueuedJunk,
    Guid? TargetPlayerId,
    bool IsConnected,
    long LastInputSequence);

public sealed record PileUpMatchState(
    Guid MatchId,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset RoundEndsAtUtc,
    DateTimeOffset LastSimulationAtUtc,
    bool IsRoundComplete,
    Guid? RoundWinnerId,
    PileUpOptions Options,
    IReadOnlyList<PilePlayerRuntimeState> Players);

public sealed record PilePlayerRuntimeState(
    PileArenaState Arena,
    long LastSequence,
    IReadOnlyList<DateTimeOffset> InputTimes,
    IReadOnlyList<DateTimeOffset> JunkReceivedTimes,
    Guid? TargetPlayerId,
    bool IsConnected,
    DateTimeOffset? DisconnectedAtUtc,
    DateTimeOffset LastFallAtUtc,
    DateTimeOffset AbilityReadyAtUtc,
    DateTimeOffset? GroundedSinceAtUtc);
