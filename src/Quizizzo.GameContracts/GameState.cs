using System.Text.Json;

namespace Quizizzo.GameContracts;

public sealed record GameModuleState(
    int SchemaVersion,
    string Phase,
    DateTimeOffset? PhaseEndsAtUtc,
    bool IsComplete,
    JsonElement Data);

public sealed record ScoreAward(Guid PlayerId, int Points, string Reason);

public sealed record GameEvent(string Kind, JsonElement Data);

public sealed record GameTransition(
    GameModuleState State,
    IReadOnlyList<ScoreAward> ScoreAwards,
    IReadOnlyList<GameEvent> Events)
{
    public static GameTransition To(GameModuleState state) => new(state, [], []);
}

public static class GameJson
{
    public static JsonElement From<T>(T value) => JsonSerializer.SerializeToElement(value);
    public static JsonElement Empty => JsonSerializer.SerializeToElement(new { });
}
