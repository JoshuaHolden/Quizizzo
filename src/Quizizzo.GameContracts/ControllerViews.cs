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
    IReadOnlyList<GamePresentationEntry> Entries);
