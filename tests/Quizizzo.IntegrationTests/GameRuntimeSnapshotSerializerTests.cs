using Quizizzo.GameContracts;
using Quizizzo.GameEngine;
using Quizizzo.Infrastructure.Games;

namespace Quizizzo.IntegrationTests;

public sealed class GameRuntimeSnapshotSerializerTests
{
    [Fact]
    public void Snapshot_round_trip_preserves_module_state_scores_and_idempotency_results()
    {
        var instanceId = GameInstanceId.New();
        var partyId = Guid.NewGuid();
        var playerId = Guid.NewGuid();
        var commandId = GameCommandId.New();
        var updatedAt = new DateTimeOffset(2026, 8, 27, 18, 0, 0, TimeSpan.Zero);
        var commandResult = new GameCommandResult(
            commandId,
            GameCommandOutcome.Applied,
            false,
            7,
            "Results",
            null,
            [new ScoreAward(playerId, 500, "test award")],
            [new GameEvent("Revealed", GameJson.From(new { answer = 42 }))]);
        var source = new GameRuntimeSnapshot(
            instanceId,
            partyId,
            "host-user",
            "test-game",
            [new GameParticipant(playerId, "Player", 100)],
            new GameModuleState(2, "Results", null, false, GameJson.From(new { count = 3 })),
            new Dictionary<Guid, int> { [playerId] = 600 },
            new Dictionary<GameCommandId, GameCommandResult> { [commandId] = commandResult },
            7,
            updatedAt);

        var restored = GameRuntimeSnapshotSerializer.Deserialize(
            GameRuntimeSnapshotSerializer.Serialize(source));

        Assert.Equal(source.GameInstanceId, restored.GameInstanceId);
        Assert.Equal(source.PartyId, restored.PartyId);
        Assert.Equal(source.HostUserId, restored.HostUserId);
        Assert.Equal(source.GameKey, restored.GameKey);
        Assert.Equal(3, restored.ModuleState.Data.GetProperty("count").GetInt32());
        Assert.Equal(600, restored.Scores[playerId]);
        var restoredResult = restored.ProcessedCommands[commandId];
        Assert.Equal(500, Assert.Single(restoredResult.ScoreAwards).Points);
        Assert.Equal(42, Assert.Single(restoredResult.Events).Data.GetProperty("answer").GetInt32());
        Assert.Equal(updatedAt, restored.UpdatedAtUtc);
    }

    [Fact]
    public void Corrupt_or_unknown_snapshot_documents_are_rejected()
    {
        Assert.Throws<InvalidDataException>(() =>
            GameRuntimeSnapshotSerializer.Deserialize("not-json"));
        Assert.Throws<InvalidDataException>(() =>
            GameRuntimeSnapshotSerializer.Deserialize("{\"documentVersion\":999}"));
    }
}
