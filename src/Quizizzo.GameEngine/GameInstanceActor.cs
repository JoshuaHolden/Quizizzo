using System.Security.Cryptography;
using System.Text;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Quizizzo.GameContracts;

namespace Quizizzo.GameEngine;

internal sealed partial class GameInstanceActor : IAsyncDisposable
{
    private readonly IGameModule module;
    private readonly IGameStateStore store;
    private readonly TimeProvider timeProvider;
    private readonly IReadOnlyList<IGameRuntimeObserver> observers;
    private readonly Channel<WorkItem> queue;
    private readonly CancellationTokenSource lifetime = new();
    private readonly object timerGate = new();
    private readonly Task processor;
    private readonly GameRuntimeOptions options;
    private readonly ILogger<GameInstanceActor>? logger;
    private CancellationTokenSource? deadlineCancellation;
    private CancellationTokenSource? simulationCancellation;
    private DateTimeOffset? simulationScheduledForUtc;
    private GameRuntimeSnapshot snapshot;

    public GameInstanceActor(
        GameRuntimeSnapshot snapshot,
        IGameModule module,
        IGameStateStore store,
        TimeProvider timeProvider,
        IReadOnlyList<IGameRuntimeObserver> observers,
        GameRuntimeOptions options,
        ILogger<GameInstanceActor>? logger)
    {
        this.snapshot = snapshot;
        this.module = module;
        this.store = store;
        this.timeProvider = timeProvider;
        this.observers = observers;
        this.options = options;
        this.logger = logger;
        queue = Channel.CreateBounded<WorkItem>(new BoundedChannelOptions(options.CommandQueueCapacity)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait,
            AllowSynchronousContinuations = false
        });
        processor = ProcessQueueAsync();
        ScheduleDeadline();
        SynchronizeSimulationSchedule();
    }

    public async Task<GameCommandResult> ExecuteAsync(
        GameCommand command,
        CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource<GameCommandResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        await queue.Writer.WriteAsync(new CommandWorkItem(command, completion), cancellationToken);
        return await completion.Task.WaitAsync(cancellationToken);
    }

    public async Task<GameRoleView> GetViewAsync(
        GameViewRequest request,
        CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource<GameRoleView>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        await queue.Writer.WriteAsync(new ViewWorkItem(request, completion), cancellationToken);
        return await completion.Task.WaitAsync(cancellationToken);
    }

    public async Task<GameSessionStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource<GameSessionStatus>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        await queue.Writer.WriteAsync(new StatusWorkItem(completion), cancellationToken);
        return await completion.Task.WaitAsync(cancellationToken);
    }

    private async Task ProcessQueueAsync()
    {
        try
        {
            await foreach (var workItem in queue.Reader.ReadAllAsync(lifetime.Token))
            {
                try
                {
                    switch (workItem)
                    {
                        case CommandWorkItem command:
                            command.Completion.TrySetResult(await ProcessCommandAsync(command.Command));
                            break;
                        case ViewWorkItem view:
                            view.Completion.TrySetResult(CreateView(view.Request));
                            break;
                        case StatusWorkItem status:
                            status.Completion.TrySetResult(CreateStatus());
                            break;
                    }
                }
                catch (Exception exception)
                {
                    workItem.SetException(exception);
                }
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
        }
        finally
        {
            while (queue.Reader.TryRead(out var pending))
            {
                pending.SetException(new ObjectDisposedException(nameof(GameInstanceActor)));
            }
        }
    }

    private async Task<GameCommandResult> ProcessCommandAsync(GameCommand command)
    {
        if (snapshot.ProcessedCommands.TryGetValue(command.CommandId, out var previous))
        {
            return previous with { IsDuplicate = true };
        }
        if (command.Actor.Role == GameActorRole.Player &&
            snapshot.ProcessedCommands.Count >= options.MaximumProcessedCommands)
        {
            if (logger is not null)
            {
                LogCommandLimitReached(
                    logger,
                    snapshot.GameInstanceId,
                    options.MaximumProcessedCommands);
            }
            return new GameCommandResult(
                command.CommandId,
                GameCommandOutcome.Rejected,
                false,
                snapshot.Revision,
                snapshot.ModuleState.Phase,
                snapshot.ModuleState.PhaseEndsAtUtc,
                [],
                [],
                "command-capacity-exceeded",
                "This game has reached its command safety limit.");
        }

        var now = timeProvider.GetUtcNow();
        var authorizationError = ValidateCommand(command, now);
        if (authorizationError is not null)
        {
            return await PersistRejectionAsync(command.CommandId, authorizationError.Value.Code,
                authorizationError.Value.Message, now);
        }

        GameTransition transition;
        try
        {
            transition = module.Apply(
                snapshot.ModuleState,
                new GameActionContext(
                    snapshot.GameInstanceId,
                    snapshot.PartyId,
                    command.Actor,
                    now),
                command.Action);
        }
        catch (GameRuleViolationException exception)
        {
            return await PersistRejectionAsync(
                command.CommandId, exception.Code, exception.Message, now);
        }

        GameStateValidator.ValidateTransition(transition, snapshot, command.Action);
        var nextRevision = checked(snapshot.Revision + 1);
        var scores = snapshot.Scores.ToDictionary();
        foreach (var award in transition.ScoreAwards)
        {
            scores[award.PlayerId] = checked(scores[award.PlayerId] + award.Points);
        }

        var result = new GameCommandResult(
            command.CommandId,
            GameCommandOutcome.Applied,
            false,
            nextRevision,
            transition.State.Phase,
            transition.State.PhaseEndsAtUtc,
            transition.ScoreAwards.ToArray(),
            transition.Events.ToArray());
        var processed = snapshot.ProcessedCommands.ToDictionary();
        processed.Add(command.CommandId, result);
        var updated = snapshot with
        {
            ModuleState = transition.State,
            Scores = scores,
            ProcessedCommands = processed,
            Revision = nextRevision,
            UpdatedAtUtc = now
        };

        await store.SaveAsync(updated, snapshot.Revision, lifetime.Token);
        snapshot = updated;
        ScheduleDeadline();
        if (command.Action is not SimulationTickElapsedAction)
        {
            SynchronizeSimulationSchedule();
        }
        _ = NotifyObserversAsync(new GameRuntimeChange(
            snapshot.GameInstanceId,
            snapshot.PartyId,
            snapshot.GameKey,
            result,
            snapshot.ModuleState.IsComplete,
            snapshot.Scores.ToDictionary()));
        return result;
    }

    private async Task<GameCommandResult> PersistRejectionAsync(
        GameCommandId commandId,
        string errorCode,
        string errorMessage,
        DateTimeOffset now)
    {
        var nextRevision = checked(snapshot.Revision + 1);
        var result = new GameCommandResult(
            commandId,
            GameCommandOutcome.Rejected,
            false,
            nextRevision,
            snapshot.ModuleState.Phase,
            snapshot.ModuleState.PhaseEndsAtUtc,
            [],
            [],
            errorCode,
            errorMessage);
        var processed = snapshot.ProcessedCommands.ToDictionary();
        processed.Add(commandId, result);
        var updated = snapshot with
        {
            ProcessedCommands = processed,
            Revision = nextRevision,
            UpdatedAtUtc = now
        };

        await store.SaveAsync(updated, snapshot.Revision, lifetime.Token);
        snapshot = updated;
        ScheduleDeadline();
        return result;
    }

    private (string Code, string Message)? ValidateCommand(GameCommand command, DateTimeOffset now)
    {
        if (command.CommandId.Value == Guid.Empty)
        {
            return ("missing-command-id", "A non-empty idempotency command ID is required.");
        }
        if (command.GameInstanceId != snapshot.GameInstanceId || command.PartyId != snapshot.PartyId)
        {
            return ("wrong-game-instance", "The command does not belong to this party game.");
        }
        if (command.Action is null)
        {
            return ("missing-action", "A semantic game action is required.");
        }
        if (snapshot.ModuleState.IsComplete)
        {
            return ("game-complete", "This game is already complete.");
        }
        if (command.Action is InvalidGameAction invalid)
        {
            return (invalid.ErrorCode, invalid.ErrorMessage);
        }

        switch (command.Actor.Role)
        {
            case GameActorRole.Host when !string.Equals(
                command.Actor.SubjectId, snapshot.HostUserId, StringComparison.Ordinal):
                return ("host-forbidden", "Only the party owner can issue host actions.");
            case GameActorRole.Player when
                !command.Actor.TryGetPlayerId(out var playerId) ||
                !snapshot.Participants.Any(player => player.PlayerId == playerId):
                return ("player-forbidden", "Only a current game participant can issue player actions.");
            case GameActorRole.System when command.Action is not DeadlineElapsedAction:
                if (command.Action is not SimulationTickElapsedAction)
                {
                    return ("system-action-forbidden", "The engine may issue only scheduled system actions.");
                }
                break;
            case not (GameActorRole.Host or GameActorRole.Player or GameActorRole.System):
                return ("actor-role-invalid", "The game actor role is invalid.");
        }

        if (command.Action is DeadlineElapsedAction elapsed)
        {
            if (command.Actor.Role != GameActorRole.System)
            {
                return ("deadline-forbidden", "Only the engine may advance a deadline.");
            }
            if (snapshot.ModuleState.PhaseEndsAtUtc != elapsed.ScheduledForUtc)
            {
                return ("stale-deadline", "The deadline no longer belongs to the current phase.");
            }
            if (now < elapsed.ScheduledForUtc)
            {
                return ("early-deadline", "The current phase deadline has not elapsed.");
            }
        }
        else if (command.Action is SimulationTickElapsedAction simulationTick)
        {
            if (command.Actor.Role != GameActorRole.System)
            {
                return ("simulation-tick-forbidden", "Only the engine may advance simulation time.");
            }
            if (module is not IGameSimulationModule || simulationScheduledForUtc != simulationTick.ScheduledForUtc)
            {
                return ("stale-simulation-tick", "The simulation tick is no longer current.");
            }
            if (now < simulationTick.ScheduledForUtc)
            {
                return ("early-simulation-tick", "The simulation tick is not due yet.");
            }
        }
        else if (snapshot.ModuleState.PhaseEndsAtUtc is { } deadline && now >= deadline)
        {
            return ("action-too-late", "The current phase deadline has elapsed.");
        }

        return null;
    }

    private GameRoleView CreateView(GameViewRequest request)
    {
        Guid? playerId = null;
        switch (request.Role)
        {
            case GameAudienceRole.Host when !string.Equals(
                request.SubjectId, snapshot.HostUserId, StringComparison.Ordinal):
                throw new UnauthorizedAccessException("Only the party owner can read the host view.");
            case GameAudienceRole.Host:
                break;
            case GameAudienceRole.Display when string.IsNullOrWhiteSpace(request.SubjectId):
                throw new UnauthorizedAccessException("A paired display identity is required.");
            case GameAudienceRole.Display:
                break;
            case GameAudienceRole.Player:
                if (!Guid.TryParse(request.SubjectId, out var parsedPlayerId) ||
                    !snapshot.Participants.Any(player => player.PlayerId == parsedPlayerId))
                {
                    throw new UnauthorizedAccessException("Only a current participant can read a player view.");
                }
                playerId = parsedPlayerId;
                break;
            default:
                throw new UnauthorizedAccessException("The requested game audience role is invalid.");
        }

        var payload = module.CreateView(
            snapshot.ModuleState,
            new GameViewContext(request.Role, request.SubjectId, playerId));
        if (payload.Data.ValueKind == System.Text.Json.JsonValueKind.Undefined)
        {
            throw new InvalidOperationException(
                $"Game module '{snapshot.GameKey}' returned an undefined role view.");
        }

        return new GameRoleView(
            snapshot.GameInstanceId,
            snapshot.PartyId,
            snapshot.GameKey,
            request.Role,
            playerId,
            snapshot.ModuleState.Phase,
            snapshot.Revision,
            snapshot.ModuleState.PhaseEndsAtUtc,
            snapshot.ModuleState.IsComplete,
            payload.Data,
            snapshot.Scores.ToDictionary());
    }

    private GameSessionStatus CreateStatus() => new(
        snapshot.GameInstanceId,
        snapshot.PartyId,
        snapshot.GameKey,
        snapshot.ModuleState.Phase,
        snapshot.Revision,
        snapshot.ModuleState.PhaseEndsAtUtc,
        snapshot.ModuleState.IsComplete);

    private void ScheduleDeadline()
    {
        CancellationTokenSource? previous;
        lock (timerGate)
        {
            previous = deadlineCancellation;
            deadlineCancellation = null;
        }
        previous?.Cancel();
        previous?.Dispose();

        if (snapshot.ModuleState.IsComplete || snapshot.ModuleState.PhaseEndsAtUtc is not { } deadline)
        {
            return;
        }

        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        lock (timerGate)
        {
            deadlineCancellation = cancellation;
        }
        _ = RunDeadlineAsync(deadline, snapshot.Revision, cancellation.Token);
    }

    private async Task RunDeadlineAsync(
        DateTimeOffset deadline,
        long revision,
        CancellationToken cancellationToken)
    {
        try
        {
            while (true)
            {
                var delay = deadline - timeProvider.GetUtcNow();
                if (delay <= TimeSpan.Zero)
                {
                    break;
                }

                await Task.Delay(delay, timeProvider, cancellationToken);
            }

            var command = new GameCommand(
                CreateDeadlineCommandId(snapshot.GameInstanceId, deadline, revision),
                snapshot.GameInstanceId,
                snapshot.PartyId,
                GameActor.SystemActor,
                new DeadlineElapsedAction(deadline));
            await ExecuteAsync(command, cancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
        catch (ChannelClosedException)
        {
        }
        catch (Exception exception)
        {
            if (logger is not null)
            {
                LogDeadlineFailure(logger, exception, snapshot.GameInstanceId, revision);
            }
        }
    }

    private static GameCommandId CreateDeadlineCommandId(
        GameInstanceId gameInstanceId,
        DateTimeOffset deadline,
        long revision)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(
            $"{gameInstanceId.Value:N}:{deadline.UtcTicks}:{revision}"));
        return new GameCommandId(new Guid(bytes.AsSpan(0, 16)));
    }

    private void SynchronizeSimulationSchedule()
    {
        var interval = GetValidatedSimulationInterval(module, snapshot.ModuleState);

        CancellationTokenSource? previous = null;
        DateTimeOffset? scheduled = null;
        CancellationTokenSource? scheduledCancellation = null;
        lock (timerGate)
        {
            if (interval is null)
            {
                previous = simulationCancellation;
                simulationCancellation = null;
                simulationScheduledForUtc = null;
            }
            else if (simulationCancellation is null)
            {
                scheduled = timeProvider.GetUtcNow().Add(interval.Value);
                simulationScheduledForUtc = scheduled;
                simulationCancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
                scheduledCancellation = simulationCancellation;
            }
        }
        previous?.Cancel();
        if (scheduled is { } next)
        {
            _ = RunSimulationTickAsync(next, scheduledCancellation!);
        }
    }

    internal static TimeSpan? GetValidatedSimulationInterval(
        IGameModule module,
        GameModuleState state)
    {
        var interval = state.IsComplete
            ? null
            : (module as IGameSimulationModule)?.GetSimulationInterval(state);
        if (interval is { } configured &&
            (configured < TimeSpan.FromMilliseconds(20) || configured > TimeSpan.FromSeconds(5)))
        {
            throw new InvalidOperationException(
                $"Game module '{module.Descriptor.Key}' requested an invalid simulation interval.");
        }
        return interval;
    }

    private async Task RunSimulationTickAsync(
        DateTimeOffset scheduledForUtc,
        CancellationTokenSource cancellation)
    {
        var cancellationToken = cancellation.Token;
        try
        {
            while (true)
            {
                var delay = scheduledForUtc - timeProvider.GetUtcNow();
                if (delay <= TimeSpan.Zero)
                {
                    break;
                }
                await Task.Delay(delay, timeProvider, cancellationToken);
            }
            var command = new GameCommand(
                CreateSimulationCommandId(snapshot.GameInstanceId, scheduledForUtc),
                snapshot.GameInstanceId,
                snapshot.PartyId,
                GameActor.SystemActor,
                new SimulationTickElapsedAction(scheduledForUtc));
            await ExecuteAsync(command, cancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
        catch (ChannelClosedException)
        {
        }
        catch (Exception exception)
        {
            if (logger is not null)
            {
                LogSimulationFailure(logger, exception, snapshot.GameInstanceId, scheduledForUtc);
            }
        }
        finally
        {
            lock (timerGate)
            {
                if (ReferenceEquals(simulationCancellation, cancellation))
                {
                    simulationCancellation = null;
                    simulationScheduledForUtc = null;
                }
            }
            cancellation.Dispose();
            if (!lifetime.IsCancellationRequested)
            {
                SynchronizeSimulationSchedule();
            }
        }
    }

    private static GameCommandId CreateSimulationCommandId(
        GameInstanceId gameInstanceId,
        DateTimeOffset scheduledForUtc)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(
            $"simulation:{gameInstanceId.Value:N}:{scheduledForUtc.UtcTicks}"));
        return new GameCommandId(new Guid(bytes.AsSpan(0, 16)));
    }

    private async Task NotifyObserversAsync(GameRuntimeChange change)
    {
        foreach (var observer in observers)
        {
            try
            {
                await observer.StateChangedAsync(change);
            }
            catch (Exception exception)
            {
                // An observer is a notification side effect; the authoritative snapshot is already saved.
                if (logger is not null)
                {
                    LogObserverFailure(
                        logger,
                        exception,
                        observer.GetType().Name,
                        change.GameInstanceId);
                }
            }
        }
    }

    [LoggerMessage(
        EventId = 1001,
        Level = LogLevel.Warning,
        Message = "Game {GameInstanceId} reached its processed command limit of {CommandLimit}")]
    private static partial void LogCommandLimitReached(
        ILogger logger,
        GameInstanceId gameInstanceId,
        int commandLimit);

    [LoggerMessage(
        EventId = 1002,
        Level = LogLevel.Error,
        Message = "Deadline processing failed for game {GameInstanceId} at revision {Revision}")]
    private static partial void LogDeadlineFailure(
        ILogger logger,
        Exception exception,
        GameInstanceId gameInstanceId,
        long revision);

    [LoggerMessage(
        EventId = 1003,
        Level = LogLevel.Error,
        Message = "Runtime observer {ObserverType} failed for game {GameInstanceId}")]
    private static partial void LogObserverFailure(
        ILogger logger,
        Exception exception,
        string observerType,
        GameInstanceId gameInstanceId);

    [LoggerMessage(
        EventId = 1004,
        Level = LogLevel.Error,
        Message = "Simulation processing failed for game {GameInstanceId} at {ScheduledForUtc}")]
    private static partial void LogSimulationFailure(
        ILogger logger,
        Exception exception,
        GameInstanceId gameInstanceId,
        DateTimeOffset scheduledForUtc);

    public async ValueTask DisposeAsync()
    {
        lifetime.Cancel();
        queue.Writer.TryComplete();
        lock (timerGate)
        {
            deadlineCancellation?.Cancel();
            deadlineCancellation?.Dispose();
            deadlineCancellation = null;
            simulationCancellation?.Cancel();
            simulationCancellation = null;
            simulationScheduledForUtc = null;
        }
        await processor;
        lifetime.Dispose();
    }

    private abstract record WorkItem
    {
        public abstract void SetException(Exception exception);
    }

    private sealed record CommandWorkItem(
        GameCommand Command,
        TaskCompletionSource<GameCommandResult> Completion) : WorkItem
    {
        public override void SetException(Exception exception) => Completion.TrySetException(exception);
    }

    private sealed record ViewWorkItem(
        GameViewRequest Request,
        TaskCompletionSource<GameRoleView> Completion) : WorkItem
    {
        public override void SetException(Exception exception) => Completion.TrySetException(exception);
    }

    private sealed record StatusWorkItem(
        TaskCompletionSource<GameSessionStatus> Completion) : WorkItem
    {
        public override void SetException(Exception exception) => Completion.TrySetException(exception);
    }
}
