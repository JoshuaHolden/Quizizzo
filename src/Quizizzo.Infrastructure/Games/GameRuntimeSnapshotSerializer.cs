using System.Text.Json;
using Quizizzo.GameContracts;
using Quizizzo.GameEngine;

namespace Quizizzo.Infrastructure.Games;

public static class GameRuntimeSnapshotSerializer
{
    private const int CurrentDocumentVersion = 1;
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public static string Serialize(GameRuntimeSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var document = new SnapshotDocument(
            CurrentDocumentVersion,
            snapshot.GameInstanceId.Value,
            snapshot.PartyId,
            snapshot.HostUserId,
            snapshot.GameKey,
            snapshot.Participants.ToArray(),
            new ModuleStateDocument(
                snapshot.ModuleState.SchemaVersion,
                snapshot.ModuleState.Phase,
                snapshot.ModuleState.PhaseEndsAtUtc,
                snapshot.ModuleState.IsComplete,
                snapshot.ModuleState.Data.GetRawText()),
            snapshot.Scores.Select(score => new PlayerScoreDocument(score.Key, score.Value)).ToArray(),
            snapshot.ProcessedCommands.Select(command => new ProcessedCommandDocument(
                command.Key.Value,
                command.Value.Outcome,
                command.Value.Revision,
                command.Value.Phase,
                command.Value.PhaseEndsAtUtc,
                command.Value.ScoreAwards.ToArray(),
                command.Value.Events.Select(gameEvent =>
                    new GameEventDocument(gameEvent.Kind, gameEvent.Data.GetRawText())).ToArray(),
                command.Value.ErrorCode,
                command.Value.ErrorMessage)).ToArray(),
            snapshot.Revision,
            snapshot.UpdatedAtUtc);
        return JsonSerializer.Serialize(document, SerializerOptions);
    }

    public static GameRuntimeSnapshot Deserialize(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        SnapshotDocument document;
        try
        {
            document = JsonSerializer.Deserialize<SnapshotDocument>(json, SerializerOptions)
                ?? throw new InvalidDataException("The game snapshot document is empty.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("The game snapshot document is invalid.", exception);
        }
        if (document.DocumentVersion != CurrentDocumentVersion ||
            document.GameInstanceId == Guid.Empty || document.PartyId == Guid.Empty ||
            string.IsNullOrWhiteSpace(document.HostUserId) || string.IsNullOrWhiteSpace(document.GameKey))
        {
            throw new InvalidDataException("The game snapshot document version or identity is invalid.");
        }

        var moduleData = ParseElement(document.ModuleState.DataJson, "module state");
        var moduleState = new GameModuleState(
            document.ModuleState.SchemaVersion,
            document.ModuleState.Phase,
            document.ModuleState.PhaseEndsAtUtc,
            document.ModuleState.IsComplete,
            moduleData);
        var processed = document.ProcessedCommands.ToDictionary(
            command => new GameCommandId(command.CommandId),
            command => new GameCommandResult(
                new GameCommandId(command.CommandId),
                command.Outcome,
                false,
                command.Revision,
                command.Phase,
                command.PhaseEndsAtUtc,
                command.ScoreAwards,
                command.Events.Select(gameEvent => new GameEvent(
                    gameEvent.Kind,
                    ParseElement(gameEvent.DataJson, "game event"))).ToArray(),
                command.ErrorCode,
                command.ErrorMessage));
        return new GameRuntimeSnapshot(
            new GameInstanceId(document.GameInstanceId),
            document.PartyId,
            document.HostUserId,
            document.GameKey,
            document.Participants,
            moduleState,
            document.Scores.ToDictionary(score => score.PlayerId, score => score.Score),
            processed,
            document.Revision,
            document.UpdatedAtUtc);
    }

    private static JsonElement ParseElement(string json, string description)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.Clone();
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"The persisted {description} JSON is invalid.", exception);
        }
    }

    private sealed record SnapshotDocument(
        int DocumentVersion,
        Guid GameInstanceId,
        Guid PartyId,
        string HostUserId,
        string GameKey,
        IReadOnlyList<GameParticipant> Participants,
        ModuleStateDocument ModuleState,
        IReadOnlyList<PlayerScoreDocument> Scores,
        IReadOnlyList<ProcessedCommandDocument> ProcessedCommands,
        long Revision,
        DateTimeOffset UpdatedAtUtc);

    private sealed record ModuleStateDocument(
        int SchemaVersion,
        string Phase,
        DateTimeOffset? PhaseEndsAtUtc,
        bool IsComplete,
        string DataJson);

    private sealed record PlayerScoreDocument(Guid PlayerId, int Score);

    private sealed record ProcessedCommandDocument(
        Guid CommandId,
        GameCommandOutcome Outcome,
        long Revision,
        string Phase,
        DateTimeOffset? PhaseEndsAtUtc,
        IReadOnlyList<ScoreAward> ScoreAwards,
        IReadOnlyList<GameEventDocument> Events,
        string? ErrorCode,
        string? ErrorMessage);

    private sealed record GameEventDocument(string Kind, string DataJson);
}
