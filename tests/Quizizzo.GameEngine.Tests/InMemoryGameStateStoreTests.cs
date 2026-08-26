using Quizizzo.GameContracts;
using Quizizzo.GameEngine;

namespace Quizizzo.GameEngine.Tests;

public sealed class InMemoryGameStateStoreTests
{
    [Fact]
    public async Task Save_requires_the_expected_revision()
    {
        var store = new InMemoryGameStateStore();
        var snapshot = CreateSnapshot();
        await store.CreateAsync(snapshot);
        await store.SaveAsync(snapshot with { Revision = 1 }, expectedRevision: 0);

        await Assert.ThrowsAsync<GameStateConcurrencyException>(() =>
            store.SaveAsync(snapshot with { Revision = 2 }, expectedRevision: 0));
    }

    [Fact]
    public async Task Create_rejects_an_existing_game_instance()
    {
        var store = new InMemoryGameStateStore();
        var snapshot = CreateSnapshot();
        await store.CreateAsync(snapshot);

        await Assert.ThrowsAsync<GameInstanceAlreadyExistsException>(() =>
            store.CreateAsync(snapshot));
    }

    private static GameRuntimeSnapshot CreateSnapshot()
    {
        var playerId = Guid.NewGuid();
        return new GameRuntimeSnapshot(
            GameInstanceId.New(),
            Guid.NewGuid(),
            "host-user",
            "test-game",
            [new GameParticipant(playerId, "Player")],
            new GameModuleState(1, "Collecting", null, false, GameJson.Empty),
            new Dictionary<Guid, int> { [playerId] = 0 },
            new Dictionary<GameCommandId, GameCommandResult>(),
            0,
            DateTimeOffset.UtcNow);
    }
}
