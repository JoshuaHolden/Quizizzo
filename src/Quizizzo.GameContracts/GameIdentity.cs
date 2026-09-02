using System.Text.Json;

namespace Quizizzo.GameContracts;

public readonly record struct GameInstanceId(Guid Value)
{
    public static GameInstanceId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString("N");
}

public readonly record struct GameCommandId(Guid Value)
{
    public static GameCommandId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString("N");
}

public sealed record GameDescriptor(
    string Key,
    string DisplayName,
    int MinimumPlayers,
    int MaximumPlayers);

public sealed record GameParticipant(
    Guid PlayerId,
    string DisplayName,
    int StartingScore = 0);

public sealed record GameStartContext(
    GameInstanceId GameInstanceId,
    Guid PartyId,
    string HostUserId,
    IReadOnlyList<GameParticipant> Participants,
    DateTimeOffset StartedAtUtc,
    JsonElement Configuration = default);
