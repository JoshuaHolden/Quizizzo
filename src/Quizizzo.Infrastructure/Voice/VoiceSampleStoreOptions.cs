namespace Quizizzo.Infrastructure.Voice;

public sealed class VoiceSampleStoreOptions
{
    public const string SectionName = "VoiceSamples";
    public const int MinimumAssetBytes = 1024;
    public const int MaximumConfiguredAssetBytes = 10 * 1024 * 1024;

    public string RootPath { get; set; } = "assets/voice-samples";
    public int MaximumAssetBytes { get; set; } = 2 * 1024 * 1024;
    public TimeSpan RetentionPeriod { get; set; } = TimeSpan.FromDays(1);
    public TimeSpan CleanupInterval { get; set; } = TimeSpan.FromHours(1);
}