namespace Quizizzo.Infrastructure.Drawings;

public sealed class DrawingAssetStoreOptions
{
    public const string SectionName = "DrawingAssets";
    public const int MinimumAssetBytes = 1024;
    public const int MaximumConfiguredAssetBytes = 20 * 1024 * 1024;

    public string RootPath { get; set; } = "assets/drawings";

    public int MaximumAssetBytes { get; set; } = 2 * 1024 * 1024;

    public TimeSpan RetentionPeriod { get; set; } = TimeSpan.FromDays(1);

    public TimeSpan CleanupInterval { get; set; } = TimeSpan.FromHours(1);
}
