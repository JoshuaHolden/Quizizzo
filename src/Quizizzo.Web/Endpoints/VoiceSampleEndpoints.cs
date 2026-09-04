using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Quizizzo.Application.Abstractions;
using Quizizzo.Application.Displays;
using Quizizzo.Application.Games;
using Quizizzo.Application.Players;
using Quizizzo.Domain.Voice;
using Quizizzo.GameContracts;
using Quizizzo.Games.VoiceChoon;
using Quizizzo.Web.Realtime;
using Quizizzo.Web.Voice;

namespace Quizizzo.Web.Endpoints;

public static class VoiceSampleEndpoints
{
    private const long MaximumSampleBytes = 2 * 1024 * 1024;
    private const long MaximumRequestBytes = MaximumSampleBytes + 64 * 1024;

    public static IEndpointRouteBuilder MapVoiceSampleEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/voicechoon/samples", SubmitAsync)
            .WithMetadata(new RequestSizeLimitAttribute(MaximumRequestBytes))
            .RequireRateLimiting("voice-sample-submit");
        endpoints.MapGet("/api/voicechoon/samples/{assetId:guid}", GetAsync)
            .RequireRateLimiting("voice-samples");
        endpoints.MapGet("/api/voicechoon/display-samples/{assetId:guid}", GetDisplayAsync)
            .RequireRateLimiting("voice-samples");
        return endpoints;
    }

    private static async Task<IResult> SubmitAsync(
        HttpContext context,
        PlayerService players,
        PartyGameService games,
        IVoiceSampleStore sampleStore,
        IVoiceSampleMetadataRepository metadata,
        IPartyRealtimeNotifier notifier)
    {
        if (context.Request.ContentLength is > MaximumRequestBytes || !context.Request.HasFormContentType ||
            !string.Equals(context.Request.Headers.XRequestedWith, "QuizizzoVoiceController", StringComparison.Ordinal))
        {
            return Results.BadRequest("A bounded VoiceChoon sample submission is required.");
        }
        if (!context.Request.Cookies.TryGetValue(PlayerSessionEndpoints.PlayerCookieName, out var playerToken) ||
            string.IsNullOrWhiteSpace(playerToken))
        {
            return Results.Unauthorized();
        }

        try
        {
            var player = await players.ReconnectAsync(playerToken, context.RequestAborted);
            var form = await context.Request.ReadFormAsync(context.RequestAborted);
            if (!Guid.TryParse(form["gameInstanceId"], out var gameInstanceId) || gameInstanceId == Guid.Empty ||
                !Guid.TryParse(form["commandId"], out var commandId) || commandId == Guid.Empty ||
                string.IsNullOrWhiteSpace(form["promptKey"]))
            {
                return Results.BadRequest("Valid game, command, and prompt identifiers are required.");
            }
            var promptKey = form["promptKey"].ToString();
            var gameView = await games.GetPlayerViewAsync(player.PlayerId, context.RequestAborted)
                ?? throw new InvalidOperationException("There is no active game.");
            var playerView = gameView.Data.Deserialize<PlayerGameViewPayload>()
                ?? throw new InvalidOperationException("The player game view is invalid.");
            var recording = playerView.Controller.Configuration.Deserialize<RecordingControllerConfiguration>();
            if (gameView.GameInstanceId != gameInstanceId ||
                !string.Equals(gameView.GameKey, VoiceChoonGameDefinition.GameKey, StringComparison.Ordinal) ||
                playerView.Controller.Kind != PlayerControllerKind.Recording ||
                !playerView.Controller.IsEnabled || recording is null ||
                !recording.Prompts.Any(prompt => string.Equals(prompt.Key, promptKey, StringComparison.Ordinal)))
            {
                return Results.BadRequest("This sample does not belong to the active VoiceChoon recording task.");
            }

            var existing = await metadata.FindSubmissionAsync(
                commandId, gameInstanceId, player.PlayerId, promptKey, context.RequestAborted);
            var registered = existing ?? await SaveAsync(
                form.Files.GetFile("sample"), commandId, player.PartyId, gameInstanceId,
                player.PlayerId, promptKey, sampleStore, metadata, context.RequestAborted);
            var result = await games.ExecutePlayerActionAsync(
                player.PlayerId,
                commandId,
                RegisterVoiceSampleAction.ActionKind,
                GameJson.From(new { promptKey, assetId = registered.Id }),
                cancellationToken: context.RequestAborted);
            if (!result.Applied)
            {
                return Results.BadRequest(result.ErrorMessage ?? "The VoiceChoon sample was rejected.");
            }

            await notifier.PartyChangedAsync(player.PartyId, "VoiceSampleRegistered", context.RequestAborted);
            return Results.Ok(new { assetId = registered.Id, duplicate = result.IsDuplicate });
        }
        catch (PlayerSessionNotFoundException)
        {
            return Results.Unauthorized();
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidDataException or
                                           InvalidOperationException or GameRuleViolationException)
        {
            return Results.BadRequest(exception.Message);
        }
    }

    private static async Task<VoiceSampleMetadata> SaveAsync(
        IFormFile? file,
        Guid submissionId,
        Guid partyId,
        Guid gameInstanceId,
        Guid playerId,
        string promptKey,
        IVoiceSampleStore sampleStore,
        IVoiceSampleMetadataRepository metadata,
        CancellationToken cancellationToken)
    {
        if (file is null || file.Length is <= 0 or > MaximumSampleBytes)
        {
            throw new InvalidDataException("Record one bounded VoiceChoon sample.");
        }
        var contentType = NormalizeContentType(file.ContentType);
        await using var memory = new MemoryStream((int)file.Length);
        await file.CopyToAsync(memory, cancellationToken);
        var bytes = memory.ToArray();
        if (contentType is "audio/wav" or "audio/wave" or "audio/x-wav")
        {
            bytes = VoiceSampleProcessor.CleanPcmWave(bytes);
            contentType = "audio/wav";
        }
        var stored = await sampleStore.SaveAsync(new VoiceSampleUpload(bytes, contentType), cancellationToken);
        try
        {
            var record = VoiceSampleMetadata.Create(
                submissionId, partyId, gameInstanceId, playerId, promptKey, stored.Key,
                stored.ContentType, stored.Length, stored.CreatedAtUtc, stored.ExpiresAtUtc);
            if (await metadata.TryAddAsync(record, cancellationToken))
            {
                return record;
            }
            await sampleStore.DeleteAsync(stored.Key, CancellationToken.None);
            return await metadata.FindSubmissionAsync(
                submissionId, gameInstanceId, playerId, promptKey, cancellationToken)
                ?? throw new InvalidOperationException("The concurrent VoiceChoon sample retry was not registered.");
        }
        catch
        {
            await sampleStore.DeleteAsync(stored.Key, CancellationToken.None);
            throw;
        }
    }

    private static async Task<IResult> GetAsync(
        Guid assetId,
        HttpContext context,
        PlayerService players,
        IVoiceSampleMetadataRepository metadata,
        IVoiceSampleStore sampleStore,
        TimeProvider timeProvider)
    {
        try
        {
            var playerToken = context.Request.Cookies[PlayerSessionEndpoints.PlayerCookieName]
                ?? throw new UnauthorizedAccessException("A valid player session is required.");
            var player = await players.ReconnectAsync(playerToken, context.RequestAborted);
            var record = await metadata.GetByIdAsync(assetId, context.RequestAborted);
            if (record is null || record.PlayerId != player.PlayerId || record.PartyId != player.PartyId ||
                record.ExpiresAtUtc <= timeProvider.GetUtcNow())
            {
                return Results.NotFound();
            }
            var sample = await sampleStore.GetAsync(record.StorageKey, context.RequestAborted);
            if (sample is null)
            {
                return Results.NotFound();
            }
            context.Response.Headers.CacheControl = "private, max-age=300";
            return Results.File(sample.Content.ToArray(), sample.ContentType, enableRangeProcessing: true);
        }
        catch (Exception exception) when (exception is PlayerSessionNotFoundException or UnauthorizedAccessException)
        {
            return Results.Unauthorized();
        }
    }

    private static async Task<IResult> GetDisplayAsync(
        Guid assetId,
        HttpContext context,
        DisplaySessionService displays,
        IVoiceSampleMetadataRepository metadata,
        IVoiceSampleStore sampleStore,
        TimeProvider timeProvider)
    {
        try
        {
            var sessionToken = context.Request.Cookies[HostDisplayEndpoints.DisplayCookieName]
                ?? throw new UnauthorizedAccessException("A valid display session is required.");
            var display = await displays.ReconnectAsync(sessionToken, context.RequestAborted);
            if (display.PartyId is not { } partyId)
            {
                return Results.Unauthorized();
            }
            var record = await metadata.GetByIdAsync(assetId, context.RequestAborted);
            if (record is null || record.PartyId != partyId || record.ExpiresAtUtc <= timeProvider.GetUtcNow())
            {
                return Results.NotFound();
            }
            var sample = await sampleStore.GetAsync(record.StorageKey, context.RequestAborted);
            return sample is null
                ? Results.NotFound()
                : Results.File(sample.Content.ToArray(), sample.ContentType, enableRangeProcessing: true);
        }
        catch (Exception exception) when (exception is InvalidOperationException or UnauthorizedAccessException)
        {
            return Results.Unauthorized();
        }
    }

    private static string NormalizeContentType(string contentType)
    {
        var value = contentType.Split(';', 2)[0].Trim().ToLowerInvariant();
        return value switch
        {
            "audio/webm" or "audio/ogg" or "audio/wav" or "audio/wave" or "audio/x-wav" or
                "audio/mp4" or "audio/m4a" => value,
            _ => throw new InvalidDataException("Recordings must use a supported browser audio format.")
        };
    }
}
