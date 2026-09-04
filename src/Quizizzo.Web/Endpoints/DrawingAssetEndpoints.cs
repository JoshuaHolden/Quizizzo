using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Quizizzo.Application.Abstractions;
using Quizizzo.Application.Games;
using Quizizzo.Application.Players;
using Quizizzo.Domain.Drawings;
using Quizizzo.GameContracts;
using Quizizzo.Games.AniMates;
using Quizizzo.Web.Drawing;
using Quizizzo.Web.Realtime;

namespace Quizizzo.Web.Endpoints;

public static class DrawingAssetEndpoints
{
    private const long MaximumFrameBytes = 2 * 1024 * 1024;
    private const long MaximumRequestBytes = AniMatesGameModule.MaximumSubmissionPayloadBytes + 64 * 1024;

    public static IEndpointRouteBuilder MapDrawingAssetEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/drawing-submissions/animates", SubmitAniMatesAsync)
            .WithMetadata(new RequestSizeLimitAttribute(MaximumRequestBytes))
            .RequireRateLimiting("drawing-submit");
        endpoints.MapGet("/api/drawing-assets/{assetId:guid}", GetAssetAsync)
            .RequireRateLimiting("drawing-assets");
        return endpoints;
    }

    private static async Task<IResult> SubmitAniMatesAsync(
        HttpContext context,
        PlayerService players,
        PartyGameService games,
        IDrawingAssetStore assetStore,
        IDrawingAssetMetadataRepository metadata,
        IPartyRealtimeNotifier notifier)
    {
        if (context.Request.ContentLength is > MaximumRequestBytes)
        {
            return Results.BadRequest("The drawing submission is too large.");
        }
        if (!context.Request.HasFormContentType)
        {
            return Results.BadRequest("A multipart drawing submission is required.");
        }
        if (!string.Equals(
            context.Request.Headers.XRequestedWith,
            "QuizizzoDrawingController",
            StringComparison.Ordinal))
        {
            return Results.BadRequest("The drawing submission request is invalid.");
        }

        try
        {
            var playerToken = context.Request.Cookies[PlayerSessionEndpoints.PlayerCookieName]
                ?? throw new UnauthorizedAccessException("A valid player session is required.");
            var player = await players.ReconnectAsync(playerToken, context.RequestAborted);
            var form = await context.Request.ReadFormAsync(context.RequestAborted);
            if (!Guid.TryParse(form["gameInstanceId"], out var gameInstanceId) ||
                !Guid.TryParse(form["commandId"], out var commandId) ||
                gameInstanceId == Guid.Empty || commandId == Guid.Empty)
            {
                return Results.BadRequest("Valid game and submission identifiers are required.");
            }
            var roundId = form["roundId"].ToString();
            var gameView = await games.GetPlayerViewAsync(player.PlayerId, context.RequestAborted)
                ?? throw new InvalidOperationException("There is no active game.");
            var playerView = gameView.Data.Deserialize<PlayerGameViewPayload>()
                ?? throw new InvalidOperationException("The player game view is invalid.");
            if (gameView.GameInstanceId != gameInstanceId ||
                !string.Equals(gameView.GameKey, AniMatesGameModule.GameKey, StringComparison.Ordinal))
            {
                return Results.BadRequest("This drawing does not belong to the active AniMates round.");
            }

            var existing = await metadata.ListSubmissionAsync(
                commandId, gameInstanceId, player.PlayerId, roundId, context.RequestAborted);
            IReadOnlyList<DrawingAssetMetadata> registered;
            if (existing.Count > 0)
            {
                registered = existing;
            }
            else
            {
                var drawing = playerView.Controller.Configuration.Deserialize<DrawingControllerConfiguration>();
                if (playerView.Controller.Kind != PlayerControllerKind.Drawing ||
                    !playerView.Controller.IsEnabled ||
                    playerView.Controller.ActionKind != SubmitAnimationAction.ActionKind ||
                    drawing is null || drawing.FrameCount is < 1 or > AniMatesGameModule.MaximumFrameCount ||
                    drawing.LogicalWidth != AniMatesGameModule.LogicalSize ||
                    drawing.LogicalHeight != AniMatesGameModule.LogicalSize ||
                    !string.Equals(drawing.DraftScope, roundId, StringComparison.Ordinal))
                {
                    return Results.BadRequest("This drawing does not belong to the active AniMates round.");
                }
                var files = form.Files.GetFiles("frames");
                if (files.Count < 1 || files.Count > drawing.FrameCount ||
                    files.Sum(file => file.Length) > AniMatesGameModule.MaximumSubmissionPayloadBytes)
                {
                    return Results.BadRequest($"Submit one to {drawing.FrameCount} bounded drawing frames.");
                }
                registered = await SaveFramesAsync(
                    files,
                    commandId,
                    player.PartyId,
                    gameInstanceId,
                    player.PlayerId,
                    roundId,
                    assetStore,
                    metadata,
                    context.RequestAborted);
            }

            var result = await games.ExecutePlayerActionAsync(
                player.PlayerId,
                commandId,
                SubmitAnimationAction.ActionKind,
                GameJson.From(new { frameAssetIds = registered.Select(asset => asset.Id).ToArray() }),
                cancellationToken: context.RequestAborted);
            if (!result.Applied)
            {
                return Results.BadRequest(result.ErrorMessage ?? "The drawing submission was rejected.");
            }

            await notifier.PartyChangedAsync(player.PartyId, "AnimationSubmitted", context.RequestAborted);
            return Results.Ok(new
            {
                submitted = true,
                duplicate = result.IsDuplicate,
                frameAssetIds = registered.Select(asset => asset.Id).ToArray()
            });
        }
        catch (Exception exception) when (exception is PlayerSessionNotFoundException or
                                           UnauthorizedAccessException)
        {
            return Results.Unauthorized();
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or
                                           GameRuleViolationException or InvalidDataException)
        {
            return Results.BadRequest(exception.Message);
        }
    }

    private static async Task<IReadOnlyList<DrawingAssetMetadata>> SaveFramesAsync(
        IReadOnlyList<IFormFile> files,
        Guid submissionId,
        Guid partyId,
        Guid gameInstanceId,
        Guid playerId,
        string roundId,
        IDrawingAssetStore assetStore,
        IDrawingAssetMetadataRepository metadata,
        CancellationToken cancellationToken)
    {
        var stored = new List<DrawingAssetReference>(files.Count);
        try
        {
            foreach (var file in files)
            {
                if (file.Length is <= 0 or > MaximumFrameBytes ||
                    !string.Equals(file.ContentType, "image/png", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException("Every frame must be a bounded PNG image.");
                }
                await using var memory = new MemoryStream((int)file.Length);
                await file.CopyToAsync(memory, cancellationToken);
                var bytes = memory.ToArray();
                if (!DrawingImageValidator.IsPngWithDimensions(
                    bytes, AniMatesGameModule.LogicalSize, AniMatesGameModule.LogicalSize))
                {
                    throw new InvalidDataException("Every frame must be a 512 by 512 PNG image.");
                }
                stored.Add(await assetStore.SaveAsync(
                    new DrawingAssetUpload(bytes, "image/png"), cancellationToken));
            }

            var records = stored.Select((asset, index) => DrawingAssetMetadata.Create(
                submissionId,
                partyId,
                gameInstanceId,
                playerId,
                roundId,
                index + 1,
                asset.Key,
                asset.ContentType,
                asset.Length,
                asset.CreatedAtUtc,
                asset.ExpiresAtUtc)).ToArray();
            if (await metadata.TryAddSubmissionAsync(records, cancellationToken))
            {
                return records;
            }

            await DeleteStoredAssetsAsync(stored, assetStore);
            var existing = await metadata.ListSubmissionAsync(
                submissionId, gameInstanceId, playerId, roundId, cancellationToken);
            if (existing.Count > 0)
            {
                return existing;
            }
            throw new InvalidOperationException(
                "The drawing submission could not be registered after a concurrent retry.");
        }
        catch
        {
            await DeleteStoredAssetsAsync(stored, assetStore);
            throw;
        }
    }

    private static async Task DeleteStoredAssetsAsync(
        IEnumerable<DrawingAssetReference> assets,
        IDrawingAssetStore assetStore)
    {
        foreach (var asset in assets)
        {
            await assetStore.DeleteAsync(asset.Key, CancellationToken.None);
        }
    }

    private static async Task<IResult> GetAssetAsync(
        Guid assetId,
        HttpContext context,
        IDrawingAssetMetadataRepository metadata,
        IDrawingAssetStore assetStore,
        TimeProvider timeProvider)
    {
        var record = await metadata.GetByIdAsync(assetId, context.RequestAborted);
        if (record is null || record.ExpiresAtUtc <= timeProvider.GetUtcNow())
        {
            return Results.NotFound();
        }
        var asset = await assetStore.GetAsync(record.StorageKey, context.RequestAborted);
        if (asset is null)
        {
            return Results.NotFound();
        }
        context.Response.Headers.CacheControl = "private, max-age=300";
        return Results.File(asset.Content.ToArray(), asset.ContentType);
    }
}
