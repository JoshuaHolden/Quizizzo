namespace Quizizzo.Infrastructure.Games;

internal sealed class GameRuntimeSnapshotRecord
{
    public Guid GameInstanceId { get; set; }
    public Guid PartyId { get; set; }
    public string GameKey { get; set; } = string.Empty;
    public long Revision { get; set; }
    public bool IsComplete { get; set; }
    public string SnapshotJson { get; set; } = string.Empty;
    public DateTimeOffset UpdatedAtUtc { get; set; }
}
