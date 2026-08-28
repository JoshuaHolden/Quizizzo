using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Quizizzo.GameContracts;
using Quizizzo.GameEngine;
using Quizizzo.Infrastructure.Identity;

namespace Quizizzo.Infrastructure.Games;

public sealed class PostgreSqlGameStateStore(IServiceScopeFactory scopeFactory) : IGameStateStore
{
    public async Task CreateAsync(
        GameRuntimeSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        dbContext.GameRuntimeSnapshots.Add(ToRecord(snapshot));
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (
            exception.InnerException is PostgresException
            {
                SqlState: PostgresErrorCodes.UniqueViolation
            })
        {
            throw new GameInstanceAlreadyExistsException(snapshot.GameInstanceId);
        }
    }

    public async Task<GameRuntimeSnapshot?> LoadAsync(
        GameInstanceId gameInstanceId,
        CancellationToken cancellationToken = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var record = await dbContext.GameRuntimeSnapshots.AsNoTracking().SingleOrDefaultAsync(
            snapshot => snapshot.GameInstanceId == gameInstanceId.Value,
            cancellationToken);
        return record is null ? null : GameRuntimeSnapshotSerializer.Deserialize(record.SnapshotJson);
    }

    public async Task SaveAsync(
        GameRuntimeSnapshot snapshot,
        long expectedRevision,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var snapshotJson = GameRuntimeSnapshotSerializer.Serialize(snapshot);
        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var updated = await dbContext.GameRuntimeSnapshots
            .Where(record => record.GameInstanceId == snapshot.GameInstanceId.Value &&
                record.Revision == expectedRevision)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(record => record.PartyId, snapshot.PartyId)
                .SetProperty(record => record.GameKey, snapshot.GameKey)
                .SetProperty(record => record.Revision, snapshot.Revision)
                .SetProperty(record => record.IsComplete, snapshot.ModuleState.IsComplete)
                .SetProperty(record => record.SnapshotJson, snapshotJson)
                .SetProperty(record => record.UpdatedAtUtc, snapshot.UpdatedAtUtc),
                cancellationToken);
        if (updated > 0)
        {
            return;
        }

        var exists = await dbContext.GameRuntimeSnapshots.AsNoTracking().AnyAsync(
            record => record.GameInstanceId == snapshot.GameInstanceId.Value,
            cancellationToken);
        if (!exists)
        {
            throw new GameInstanceNotFoundException(snapshot.GameInstanceId);
        }
        throw new GameStateConcurrencyException(snapshot.GameInstanceId);
    }

    private static GameRuntimeSnapshotRecord ToRecord(GameRuntimeSnapshot snapshot) => new()
    {
        GameInstanceId = snapshot.GameInstanceId.Value,
        PartyId = snapshot.PartyId,
        GameKey = snapshot.GameKey,
        Revision = snapshot.Revision,
        IsComplete = snapshot.ModuleState.IsComplete,
        SnapshotJson = GameRuntimeSnapshotSerializer.Serialize(snapshot),
        UpdatedAtUtc = snapshot.UpdatedAtUtc
    };
}
