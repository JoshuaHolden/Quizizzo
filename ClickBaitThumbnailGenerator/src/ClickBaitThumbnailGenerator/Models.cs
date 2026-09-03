using System.Text.Json.Serialization;

namespace ClickBaitThumbnailGenerator;

public enum JobStatus
{
    Pending,
    Generating,
    Generated,
    NeedsReview,
    Failed,
    DuplicateSuspected
}

public enum ReviewStatus
{
    Pending,
    Approved,
    Rejected
}

public enum TextDetectionResult
{
    NoTextDetected,
    TextSuspected,
    CheckUnavailable
}

public enum TitleJobStatus
{
    Pending,
    Generating,
    Generated,
    Failed
}

public sealed record Scenario(
    string Id,
    string Scene,
    string NormalizedScene,
    string Category,
    string Composition,
    string VisualStyle,
    DateTimeOffset CreatedAtUtc);

public sealed record ScenarioCandidate(
    [property: JsonPropertyName("scene")] string Scene,
    [property: JsonPropertyName("category")] string Category,
    [property: JsonPropertyName("composition")] string Composition,
    [property: JsonPropertyName("visualStyle")] string VisualStyle);

public sealed record ImageJob(
    string ScenarioId,
    string Scene,
    string Category,
    string VisualStyle,
    string? Model,
    string? FullPrompt,
    DateTimeOffset? GeneratedAtUtc,
    int AttemptCount,
    string? ApiRequestId,
    JobStatus Status,
    ReviewStatus ReviewStatus,
    string? FailureReason,
    int? SourceWidth,
    int? SourceHeight,
    string? FinalFilename,
    string? Sha256,
    string? PerceptualHash,
    TextDetectionResult? TextDetectionResult,
    decimal EstimatedCost,
    DateTimeOffset UpdatedAtUtc,
    IReadOnlyList<string> AiTitles);

public sealed record TitleJob(
    string ScenarioId,
    string FinalFilename,
    TitleJobStatus Status,
    int AttemptCount,
    string? Model,
    IReadOnlyList<string> Titles,
    string? ApiRequestId,
    string? FailureReason,
    DateTimeOffset? GeneratedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record GeneratedTitles(IReadOnlyList<string> Titles, string? RequestId);

public sealed record TitleStatistics(int Total, int Pending, int Generating, int Generated, int Failed);

public sealed record GeneratedImage(byte[] Bytes, string? RequestId);

public sealed record ProcessedImage(
    int SourceWidth,
    int SourceHeight,
    string FinalFilename,
    string Sha256,
    string PerceptualHash,
    TextDetectionResult TextDetectionResult,
    bool DuplicateSuspected);

public sealed record JobStatistics(
    int Total,
    int Pending,
    int Generating,
    int Generated,
    int NeedsReview,
    int Failed,
    int DuplicateSuspected,
    int Approved,
    int Rejected,
    decimal EstimatedSpend);

public sealed record ExportManifestItem(
    string Id,
    string Image,
    string Category,
    int Width,
    int Height,
    string Sha256,
    [property: JsonPropertyName("aiTitles")] IReadOnlyList<string> AiTitles);

public sealed record ProvenanceItem(
    string Id,
    string Model,
    DateTimeOffset? GeneratedAtUtc,
    string FullPrompt,
    string Sha256,
    string PerceptualHash);
