using System.Text.Json;

namespace Quizizzo.GameContracts;

public enum PlayerControllerKind
{
    Waiting,
    Choice,
    Text,
    Number,
    Vote,
    Drawing,
    Recording,
    Rhythm,
    Arcade
}

public sealed record PlayerControllerView(
    PlayerControllerKind Kind,
    string ActionKind,
    bool IsEnabled,
    string SubmitLabel,
    JsonElement Configuration);

public sealed record NumberControllerConfiguration(
    long Minimum,
    long Maximum,
    long Step,
    string? Suffix,
    long? SubmittedValue);

public sealed record DrawingControllerConfiguration(
    int LogicalWidth,
    int LogicalHeight,
    int FrameCount,
    string DraftScope,
    bool OnionSkinEnabled = true);

public sealed record RecordingPromptConfiguration(
    string Key,
    string Label,
    string Example,
    string Style,
    int RootMidiNote,
    string Guidance,
    Guid? AssetId);

public sealed record RecordingControllerConfiguration(
    IReadOnlyList<RecordingPromptConfiguration> Prompts,
    string UploadEndpoint,
    int MaximumDurationSeconds,
    long MaximumBytesPerSample);

public sealed record RhythmControllerNote(
    Guid Id,
    int Lane,
    double StartTimeSeconds,
    double DurationSeconds,
    string Type,
    Guid? SampleAssetId,
    double PlaybackRate,
    bool Loop,
    double? LoopStartSeconds,
    double? LoopEndSeconds,
    string SoundLabel = "");

public sealed record RhythmControllerConfiguration(
    DateTimeOffset SongStartsAtUtc,
    double SongDurationSeconds,
    IReadOnlyList<RhythmControllerNote> Notes,
    long NextSequence,
    double NoteTravelSeconds = 2,
    double GoodWindowSeconds = 0.2,
    double GreatWindowSeconds = 0.12,
    double PerfectWindowSeconds = 0.06);

public sealed record VoiceChoonDisplayPlayback(
    Guid Id,
    double StartTimeSeconds,
    double DurationSeconds,
    Guid SampleAssetId,
    double PlaybackRate,
    bool Loop,
    double? LoopStartSeconds,
    double? LoopEndSeconds,
    Guid? PlayerId = null,
    Guid? JudgementNoteId = null,
    double? JudgementTimeSeconds = null,
    int Velocity = 100);

public sealed record ArcadeControl(
    string Input,
    string Label,
    string AccessibleLabel,
    int? HoldRepeatMilliseconds = null);

public sealed record ArcadeControllerConfiguration(
    IReadOnlyList<ArcadeControl> Controls,
    long NextSequence,
    IReadOnlyList<ControllerOption> Targets,
    string? SelectedTargetId,
    string? AvailableAbility,
    int ChargePercent,
    ArcadeArenaConfiguration? Arena = null,
    bool StashAvailable = false,
    ArcadeUpcomingPiece? StashedPiece = null);

public sealed record ArcadeArenaConfiguration(
    int Columns,
    int VisibleRows,
    int HiddenRows,
    IReadOnlyList<ArcadeArenaCell> SettledCells,
    ArcadeActivePiece? ActivePiece,
    IReadOnlyList<ArcadeUpcomingPiece> UpcomingPieces,
    IReadOnlyDictionary<string, IReadOnlyList<ArcadeGridPoint>> PieceShapes);

public sealed record ArcadeArenaCell(int X, int Y, string Material);

public sealed record ArcadeActivePiece(
    string PieceKey,
    string Material,
    int X,
    int Y,
    int Rotation);

public sealed record ArcadeUpcomingPiece(string PieceKey, string Material);

public sealed record ArcadeGridPoint(int X, int Y);

public sealed record ArcadeControllerSubmission(
    long Sequence,
    string Input,
    string? TargetPlayerId,
    DateTimeOffset ClientTimestamp);

public sealed record ControllerOption(
    string Id,
    string Label,
    string? Detail = null,
    IReadOnlyList<Guid>? FrameAssetIds = null,
    string? ImageUrl = null);

public sealed record GameMediaItem(
    string Id,
    string ImageUrl,
    string AlternativeText,
    string? Heading = null,
    string? Body = null,
    string? Badge = null);

public sealed record GameMediaPresentationView(
    string Mode,
    IReadOnlyList<GameMediaItem> Items);

public sealed record VoteControllerConfiguration(
    IReadOnlyList<ControllerOption> Options,
    string? SubmittedOptionId = null,
    string SelectionProperty = "optionId",
    string SelectionScope = "default");

public sealed record TextControllerConfiguration(
    int MaximumLength,
    string Placeholder,
    string? SubmittedValue = null,
    IReadOnlyList<string>? FormatSegments = null);

public sealed record TextControllerSubmission(
    string Value,
    IReadOnlyList<string> Values);

public sealed record ChoiceControllerConfiguration(
    IReadOnlyList<ControllerOption> Options,
    string? SubmittedOptionId = null,
    string SelectionProperty = "optionId",
    string SelectionScope = "default");

public sealed record DrawingAnimationView(
    Guid SubmissionPlayerId,
    string? CreatorName,
    string Prompt,
    IReadOnlyList<Guid> FrameAssetIds,
    int Votes,
    int? Rank,
    int PointsAwarded);

public sealed record DrawingPresentationView(
    string Mode,
    int FrameDurationMilliseconds,
    IReadOnlyList<DrawingAnimationView> Animations,
    int LoopsPerAnimation = 1);

public sealed record TutorialPresentationView(
    string Title,
    int FrameCount,
    IReadOnlyList<string> Steps);

public sealed record PlayerGameViewPayload(
    string Heading,
    string Instructions,
    PlayerControllerView Controller,
    JsonElement State,
    GameMediaPresentationView? Media = null,
    string ScoreUnit = "pts");

public sealed record GamePresentationEntry(
    Guid PlayerId,
    string Label,
    string Value,
    int? Rank,
    int PointsAwarded);

public sealed record GameStatisticView(
    string Label,
    string Value);

public sealed record HostGameViewPayload(
    string Title,
    string Prompt,
    string PhaseMessage,
    int SubmittedPlayers,
    int TotalPlayers,
    bool CanAdvance,
    string? AdvanceActionKind,
    string? AdvanceLabel,
    IReadOnlyList<GamePresentationEntry> Entries);

public sealed record DisplayGameViewPayload(
    string Title,
    string Prompt,
    string PhaseMessage,
    int SubmittedPlayers,
    int TotalPlayers,
    IReadOnlyList<GamePresentationEntry> Entries,
    DrawingPresentationView? Drawing = null,
    TutorialPresentationView? Tutorial = null,
    bool ShowRoundRanking = false,
    GameMediaPresentationView? Media = null,
    string ScoreUnit = "pts",
    IReadOnlyList<GameStatisticView>? Statistics = null,
    JsonElement? State = null);
