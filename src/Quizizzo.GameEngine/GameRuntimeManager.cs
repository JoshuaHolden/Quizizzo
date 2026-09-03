using Quizizzo.GameContracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Quizizzo.GameEngine;

public sealed class GameRuntimeManager(
    GameModuleCatalog modules,
    IGameStateStore stateStore,
    TimeProvider timeProvider,
    IEnumerable<IGameRuntimeObserver>? observers = null,
    IOptions<GameRuntimeOptions>? options = null,
    ILoggerFactory? loggerFactory = null) : IAsyncDisposable
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly Dictionary<GameInstanceId, GameInstanceActor> actors = [];
    private bool disposed;
    private readonly IReadOnlyList<IGameRuntimeObserver> runtimeObservers = observers?.ToArray() ?? [];
    private readonly GameRuntimeOptions runtimeOptions = ValidateOptions(options?.Value);
    private readonly ILoggerFactory? runtimeLoggerFactory = loggerFactory;

    public IReadOnlyList<GameDescriptor> ListGames() => modules.List();

    public async Task<GameSessionStatus> StartAsync(
        GameStartRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateStartRequest(request);
        var module = modules.GetRequired(request.GameKey);
        if (request.Participants.Count < module.Descriptor.MinimumPlayers ||
            request.Participants.Count > module.Descriptor.MaximumPlayers)
        {
            throw new InvalidOperationException(
                $"{module.Descriptor.DisplayName} requires between " +
                $"{module.Descriptor.MinimumPlayers} and {module.Descriptor.MaximumPlayers} players.");
        }

        await gate.WaitAsync(cancellationToken);
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (actors.ContainsKey(request.GameInstanceId))
            {
                throw new GameInstanceAlreadyExistsException(request.GameInstanceId);
            }

            var now = timeProvider.GetUtcNow();
            var participants = request.Participants.ToArray();
            var moduleState = module.Start(new GameStartContext(
                request.GameInstanceId,
                request.PartyId,
                request.HostUserId,
                participants,
                now,
                request.Configuration));
            GameStateValidator.Validate(moduleState, module.Descriptor.Key);
            _ = GameInstanceActor.GetValidatedSimulationInterval(module, moduleState);
            var snapshot = new GameRuntimeSnapshot(
                request.GameInstanceId,
                request.PartyId,
                request.HostUserId,
                module.Descriptor.Key,
                participants,
                moduleState,
                participants.ToDictionary(player => player.PlayerId, player => player.StartingScore),
                new Dictionary<GameCommandId, GameCommandResult>(),
                0,
                now);

            await stateStore.CreateAsync(snapshot, cancellationToken);
            var actor = CreateActor(snapshot, module);
            actors.Add(request.GameInstanceId, actor);
            return await actor.GetStatusAsync(cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<GameCommandResult> ExecuteAsync(
        GameCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var actor = await GetOrRecoverActorAsync(command.GameInstanceId, cancellationToken);
        return await actor.ExecuteAsync(command, cancellationToken);
    }

    public async Task<GameRoleView> GetViewAsync(
        GameInstanceId gameInstanceId,
        GameViewRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var actor = await GetOrRecoverActorAsync(gameInstanceId, cancellationToken);
        return await actor.GetViewAsync(request, cancellationToken);
    }

    public async Task<GameSessionStatus> GetStatusAsync(
        GameInstanceId gameInstanceId,
        CancellationToken cancellationToken = default)
    {
        var actor = await GetOrRecoverActorAsync(gameInstanceId, cancellationToken);
        return await actor.GetStatusAsync(cancellationToken);
    }

    private async Task<GameInstanceActor> GetOrRecoverActorAsync(
        GameInstanceId gameInstanceId,
        CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (actors.TryGetValue(gameInstanceId, out var existing))
            {
                return existing;
            }

            var snapshot = await stateStore.LoadAsync(gameInstanceId, cancellationToken)
                ?? throw new GameInstanceNotFoundException(gameInstanceId);
            var module = modules.GetRequired(snapshot.GameKey);
            GameStateValidator.Validate(snapshot.ModuleState, snapshot.GameKey);
            var recovered = CreateActor(snapshot, module);
            actors.Add(gameInstanceId, recovered);
            return recovered;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task ReleaseAsync(GameInstanceId gameInstanceId)
    {
        GameInstanceActor? actor;
        await gate.WaitAsync();
        try
        {
            if (disposed || !actors.Remove(gameInstanceId, out actor))
            {
                return;
            }
        }
        finally
        {
            gate.Release();
        }

        await actor.DisposeAsync();
    }

    private GameInstanceActor CreateActor(GameRuntimeSnapshot snapshot, IGameModule module) => new(
        snapshot,
        module,
        stateStore,
        timeProvider,
        runtimeObservers,
        runtimeOptions,
        runtimeLoggerFactory?.CreateLogger<GameInstanceActor>());

    private static GameRuntimeOptions ValidateOptions(GameRuntimeOptions? options)
    {
        var value = options ?? new GameRuntimeOptions();
        value.Validate();
        return value;
    }

    private static void ValidateStartRequest(GameStartRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.GameInstanceId.Value == Guid.Empty)
        {
            throw new ArgumentException("A game instance ID is required.", nameof(request));
        }
        if (request.PartyId == Guid.Empty)
        {
            throw new ArgumentException("A party ID is required.", nameof(request));
        }
        ArgumentException.ThrowIfNullOrWhiteSpace(request.HostUserId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.GameKey);
        ArgumentNullException.ThrowIfNull(request.Participants);
        if (request.Participants.Select(player => player.PlayerId).Distinct().Count() != request.Participants.Count)
        {
            throw new ArgumentException("Game participants must have unique player IDs.", nameof(request));
        }
        if (request.Participants.Any(player =>
                player.PlayerId == Guid.Empty || string.IsNullOrWhiteSpace(player.DisplayName)))
        {
            throw new ArgumentException("Every game participant requires an ID and display name.", nameof(request));
        }
    }

    public async ValueTask DisposeAsync()
    {
        GameInstanceActor[] active;
        await gate.WaitAsync();
        try
        {
            if (disposed)
            {
                return;
            }
            disposed = true;
            active = actors.Values.ToArray();
            actors.Clear();
        }
        finally
        {
            gate.Release();
        }

        foreach (var actor in active)
        {
            await actor.DisposeAsync();
        }
        gate.Dispose();
    }
}
