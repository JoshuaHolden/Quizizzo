namespace Quizizzo.Infrastructure.Games;

public sealed class GameStateStoreOptions
{
    public const string SectionName = "GameStateStore";

    public TimeSpan CompletedSnapshotRetention { get; set; } = TimeSpan.FromDays(30);
    public TimeSpan OrphanSnapshotRetention { get; set; } = TimeSpan.FromDays(1);
    public TimeSpan CleanupInterval { get; set; } = TimeSpan.FromHours(6);
}
