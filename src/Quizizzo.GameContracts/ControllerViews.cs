using System.Text.Json;

namespace Quizizzo.GameContracts;

public enum PlayerControllerKind
{
    Waiting,
    Choice,
    Text,
    Number,
    Vote,
    Drawing
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

public sealed record ControllerOption(
    string Id,
    string Label,
    string? Detail = null,
    IReadOnlyList<Guid>? FrameAssetIds = null);

public sealed record VoteControllerConfiguration(
    IReadOnlyList<ControllerOption> Options,
    string? SubmittedOptionId = null,
    string SelectionProperty = "optionId",
    string SelectionScope = "default");

public sealed record TextControllerConfiguration(
    int MaximumLength,
    string Placeholder,
    string? SubmittedValue = null);

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
    JsonElement State);

public sealed record GamePresentationEntry(
    Guid PlayerId,
    string Label,
    string Value,
    int? Rank,
    int PointsAwarded);

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
    bool ShowRoundRanking = false);
