using System.Collections.Concurrent;
using Quizizzo.GameContracts;

namespace Quizizzo.GameEngine;

public interface IGameStateStore
{
    Task CreateAsync(GameRuntimeSnapshot snapshot, CancellationToken cancellationToken = default);

    Task<GameRuntimeSnapshot?> LoadAsync(
        GameInstanceId gameInstanceId,
        CancellationToken cancellationToken = default);

    Task SaveAsync(
        GameRuntimeSnapshot snapshot,
        long expectedRevision,
        CancellationToken cancellationToken = default);
}

public sealed class GameInstanceAlreadyExistsException(GameInstanceId gameInstanceId)
    : InvalidOperationException($"Game instance {gameInstanceId} already exists.");

public sealed class GameInstanceNotFoundException(GameInstanceId gameInstanceId)
    : InvalidOperationException($"Game instance {gameInstanceId} was not found.");

public sealed class GameStateConcurrencyException(GameInstanceId gameInstanceId)
    : InvalidOperationException($"Game instance {gameInstanceId} was updated concurrently.");

public sealed class InMemoryGameStateStore : IGameStateStore
{
    private readonly object gate = new();
    private readonly ConcurrentDictionary<GameInstanceId, GameRuntimeSnapshot> snapshots = new();

    public Task CreateAsync(GameRuntimeSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!snapshots.TryAdd(snapshot.GameInstanceId, snapshot))
        {
            throw new GameInstanceAlreadyExistsException(snapshot.GameInstanceId);
        }

        return Task.CompletedTask;
    }

    public Task<GameRuntimeSnapshot?> LoadAsync(
        GameInstanceId gameInstanceId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        snapshots.TryGetValue(gameInstanceId, out var snapshot);
        return Task.FromResult(snapshot);
    }

    public Task SaveAsync(
        GameRuntimeSnapshot snapshot,
        long expectedRevision,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            if (!snapshots.TryGetValue(snapshot.GameInstanceId, out var current))
            {
                throw new GameInstanceNotFoundException(snapshot.GameInstanceId);
            }

            if (current.Revision != expectedRevision)
            {
                throw new GameStateConcurrencyException(snapshot.GameInstanceId);
            }

            snapshots[snapshot.GameInstanceId] = snapshot;
        }

        return Task.CompletedTask;
    }
}
