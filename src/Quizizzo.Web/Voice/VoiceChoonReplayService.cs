using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Quizizzo.Application.Abstractions;
using Quizizzo.Domain.Parties;
using Quizizzo.Domain.Voice;
using Quizizzo.Games.VoiceChoon;
using Quizizzo.Web.Presentation;

namespace Quizizzo.Web.Voice;

public sealed record VoiceChoonReplayView(
    string ShareCode,
    string Title,
    PhaserPresentationSnapshot Snapshot,
    DateTimeOffset CreatedAtUtc);

public sealed class VoiceChoonReplayService(
    IServiceScopeFactory scopeFactory,
    IVoiceSampleStore sampleStore,
    TimeProvider timeProvider) : IDisposable
{
    private readonly SemaphoreSlim saveLock = new(1, 1);

    public async Task<VoiceChoonReplayView> SaveAsync(
        Guid partyId,
        PhaserPresentationSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        if (!string.Equals(snapshot.GameKey, VoiceChoonGameDefinition.GameKey, StringComparison.Ordinal) ||
            !Guid.TryParse(snapshot.GameInstanceId, out var gameInstanceId) ||
            snapshot.Phase != VoiceChoonGameModule.ResultsPhase || snapshot.GameState is not { } stateJson)
        {
            throw new InvalidOperationException("Only a completed VoiceChoon performance can be saved.");
        }
        var state = stateJson.Deserialize<VoiceChoonDisplayState>()
            ?? throw new InvalidOperationException("The VoiceChoon replay data is invalid.");
        var sampleIds = state.Playback?.Select(note => note.SampleAssetId).Distinct().ToArray() ?? [];
        if (sampleIds.Length == 0)
        {
            throw new InvalidOperationException("The VoiceChoon replay has no recorded voices.");
        }

        await saveLock.WaitAsync(cancellationToken);
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var replays = scope.ServiceProvider.GetRequiredService<IVoiceChoonReplayRepository>();
            var existing = await replays.GetByGameInstanceAsync(gameInstanceId, cancellationToken);
            if (existing is not null)
            {
                return ToView(existing);
            }
            var parties = scope.ServiceProvider.GetRequiredService<IPartyRepository>();
            var party = await parties.GetByIdAsync(new PartyId(partyId), cancellationToken)
                ?? throw new InvalidOperationException("The replay party no longer exists.");
            var metadata = scope.ServiceProvider.GetRequiredService<IVoiceSampleMetadataRepository>();
            var samples = await metadata.RetainForReplayAsync(sampleIds, gameInstanceId, cancellationToken);
            foreach (var sample in samples)
            {
                await sampleStore.RetainAsync(sample.StorageKey, cancellationToken);
            }

            var replay = VoiceChoonReplay.Create(
                NewShareCode(), partyId, gameInstanceId, party.HostUserId,
                snapshot.Title ?? state.SongName,
                JsonSerializer.Serialize(snapshot), sampleIds, timeProvider.GetUtcNow());
            await replays.AddAsync(replay, cancellationToken);
            return ToView(replay);
        }
        finally
        {
            saveLock.Release();
        }
    }

    public async Task<VoiceChoonReplayView?> GetAsync(
        string shareCode,
        CancellationToken cancellationToken = default)
    {
        if (!IsValidShareCode(shareCode)) return null;
        await using var scope = scopeFactory.CreateAsyncScope();
        var replay = await scope.ServiceProvider.GetRequiredService<IVoiceChoonReplayRepository>()
            .GetByShareCodeAsync(shareCode, cancellationToken);
        return replay is null ? null : ToView(replay);
    }

    public async Task<VoiceSampleContent?> GetSampleAsync(
        string shareCode,
        Guid sampleAssetId,
        CancellationToken cancellationToken = default)
    {
        if (!IsValidShareCode(shareCode)) return null;
        await using var scope = scopeFactory.CreateAsyncScope();
        var replay = await scope.ServiceProvider.GetRequiredService<IVoiceChoonReplayRepository>()
            .GetByShareCodeAsync(shareCode, cancellationToken);
        if (replay is null || !replay.SampleAssetIds.Contains(sampleAssetId)) return null;
        var sample = await scope.ServiceProvider.GetRequiredService<IVoiceSampleMetadataRepository>()
            .GetByIdAsync(sampleAssetId, cancellationToken);
        return sample is null ? null : await sampleStore.GetAsync(sample.StorageKey, cancellationToken);
    }

    public async Task DeleteAsync(
        string shareCode,
        string hostUserId,
        CancellationToken cancellationToken = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var replays = scope.ServiceProvider.GetRequiredService<IVoiceChoonReplayRepository>();
        var replay = await replays.GetByShareCodeAsync(shareCode, cancellationToken)
            ?? throw new InvalidOperationException("That replay no longer exists.");
        if (!string.Equals(replay.HostUserId, hostUserId, StringComparison.Ordinal))
        {
            throw new UnauthorizedAccessException("Only the party host can delete this replay.");
        }
        var metadata = scope.ServiceProvider.GetRequiredService<IVoiceSampleMetadataRepository>();
        foreach (var sampleId in replay.SampleAssetIds)
        {
            var sample = await metadata.GetByIdAsync(sampleId, cancellationToken);
            if (sample is null) continue;
            await sampleStore.DeleteAsync(sample.StorageKey, cancellationToken);
            await metadata.DeleteAsync(sample.Id, cancellationToken);
        }
        await replays.DeleteAsync(replay, cancellationToken);
    }

    private static VoiceChoonReplayView ToView(VoiceChoonReplay replay) => new(
        replay.ShareCode,
        replay.Title,
        JsonSerializer.Deserialize<PhaserPresentationSnapshot>(replay.SnapshotJson)
            ?? throw new InvalidDataException("The saved VoiceChoon replay is invalid."),
        replay.CreatedAtUtc);

    private static string NewShareCode() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(18))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static bool IsValidShareCode(string value) =>
        value.Length is >= 16 and <= 64 && value.All(character =>
            char.IsAsciiLetterOrDigit(character) || character is '-' or '_');

    public void Dispose() => saveLock.Dispose();
}
