using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Quizizzo.Application.Abstractions;
using Quizizzo.Application.Games;
using Quizizzo.Domain.Drawings;
using Quizizzo.GameContracts;
using Quizizzo.Games.AniMates;
using Quizizzo.Web.Endpoints;

namespace Quizizzo.IntegrationTests;

public sealed class AniMatesSubmissionTests
{
    [Fact]
    public async Task Player_refreshes_before_and_after_idempotent_asset_backed_submission()
    {
        await using var baseFactory = new RecoveryWebApplicationFactory();
        var assets = new FakeDrawingAssets();
        await using var factory = Configure(baseFactory, assets);
        await StartAniMatesAsync(factory, baseFactory.State);
        using var client = PlayerClient(factory);

        var before = await client.GetStringAsync("/play");
        var commandId = Guid.NewGuid();
        using var firstResponse = await client.PostAsync(
            "/api/drawing-submissions/animates",
            SubmissionContent(baseFactory.State, commandId, ValidPng()));
        using var retryResponse = await client.PostAsync(
            "/api/drawing-submissions/animates",
            SubmissionContent(baseFactory.State, commandId, ValidPng()));
        var after = await client.GetStringAsync("/play");

        Assert.Contains("Spanking a blue dog", before, StringComparison.Ordinal);
        firstResponse.EnsureSuccessStatusCode();
        retryResponse.EnsureSuccessStatusCode();
        Assert.Contains("Animation submitted", after, StringComparison.Ordinal);
        Assert.Equal(3, assets.Metadata.Count);
        Assert.Equal(3, assets.Stored.Count);
    }

    [Fact]
    public async Task Submission_rejects_invalid_dimensions_and_oversized_frames()
    {
        await using var baseFactory = new RecoveryWebApplicationFactory();
        var assets = new FakeDrawingAssets();
        await using var factory = Configure(baseFactory, assets);
        await StartAniMatesAsync(factory, baseFactory.State);
        using var client = PlayerClient(factory);

        using var wrongSize = await client.PostAsync(
            "/api/drawing-submissions/animates",
            SubmissionContent(baseFactory.State, Guid.NewGuid(), ValidPng(width: 256)));
        using var tooLarge = await client.PostAsync(
            "/api/drawing-submissions/animates",
            SubmissionContent(baseFactory.State, Guid.NewGuid(), new byte[(2 * 1024 * 1024) + 1]));

        Assert.Equal(HttpStatusCode.BadRequest, wrongSize.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, tooLarge.StatusCode);
        Assert.Empty(assets.Metadata);
        Assert.Empty(assets.Stored);
    }

    [Fact]
    public async Task Opaque_asset_route_serves_registered_unexpired_frame()
    {
        await using var baseFactory = new RecoveryWebApplicationFactory();
        var assets = new FakeDrawingAssets();
        await using var factory = Configure(baseFactory, assets);
        await StartAniMatesAsync(factory, baseFactory.State);
        using var client = PlayerClient(factory);
        using var submission = await client.PostAsync(
            "/api/drawing-submissions/animates",
            SubmissionContent(baseFactory.State, Guid.NewGuid(), ValidPng()));
        submission.EnsureSuccessStatusCode();
        var assetId = assets.Metadata[0].Id;

        using var response = await client.GetAsync($"/api/drawing-assets/{assetId:D}");

        response.EnsureSuccessStatusCode();
        Assert.Equal("image/png", response.Content.Headers.ContentType?.MediaType);
    }

    private static WebApplicationFactory<Program> Configure(
        RecoveryWebApplicationFactory factory,
        FakeDrawingAssets assets) => factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IDrawingAssetStore>();
                services.RemoveAll<IDrawingAssetMetadataRepository>();
                services.AddSingleton<IDrawingAssetStore>(assets);
                services.AddSingleton<IDrawingAssetMetadataRepository>(assets);
            }));

    private static async Task StartAniMatesAsync(
        WebApplicationFactory<Program> factory,
        RecoveryWebApplicationFactory.RecoveryState state)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var games = scope.ServiceProvider.GetRequiredService<PartyGameService>();
        await games.StartAsync(
            state.Party.Id.Value,
            RecoveryWebApplicationFactory.HostUserId,
            AniMatesGameModule.GameKey);
        await games.ExecuteHostActionAsync(
            state.Party.Id.Value,
            RecoveryWebApplicationFactory.HostUserId,
            Guid.NewGuid(),
            AdvanceAniMatesAction.ActionKind,
            GameJson.Empty);
    }

    private static HttpClient PlayerClient(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Requested-With", "QuizizzoDrawingController");
        client.DefaultRequestHeaders.Add(
            "Cookie",
            $"{PlayerSessionEndpoints.PlayerCookieName}={RecoveryWebApplicationFactory.PlayerToken}");
        return client;
    }

    private static MultipartFormDataContent SubmissionContent(
        RecoveryWebApplicationFactory.RecoveryState state,
        Guid commandId,
        byte[] frame)
    {
        var content = new MultipartFormDataContent();
        content.Add(new StringContent(state.Party.CurrentGameInstanceId!.Value.ToString("D")), "gameInstanceId");
        content.Add(new StringContent("animates-round-1"), "roundId");
        content.Add(new StringContent(commandId.ToString("D")), "commandId");
        for (var index = 0; index < 3; index++)
        {
            var file = new ByteArrayContent(frame);
            file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            content.Add(file, "frames", $"frame-{index + 1}.png");
        }
        return content;
    }

    private static byte[] ValidPng(int width = 512, int height = 512) =>
        PngTestData.Create(width, height);

    private sealed class FakeDrawingAssets : IDrawingAssetStore, IDrawingAssetMetadataRepository
    {
        public List<DrawingAssetMetadata> Metadata { get; } = [];
        public Dictionary<string, DrawingAssetContent> Stored { get; } = [];

        public Task<DrawingAssetReference> SaveAsync(
            DrawingAssetUpload asset,
            CancellationToken cancellationToken = default)
        {
            var key = $"ab/{Guid.NewGuid():N}.png";
            Stored[key] = new DrawingAssetContent(asset.Content, asset.ContentType);
            var now = DateTimeOffset.UtcNow;
            return Task.FromResult(new DrawingAssetReference(
                key, asset.ContentType, asset.Content.Length, now, now.AddDays(1)));
        }

        public Task<DrawingAssetContent?> GetAsync(string key, CancellationToken cancellationToken = default) =>
            Task.FromResult(Stored.GetValueOrDefault(key));

        public Task DeleteAsync(string key, CancellationToken cancellationToken = default)
        {
            Stored.Remove(key);
            return Task.CompletedTask;
        }

        public Task<int> DeleteExpiredAsync(
            DateTimeOffset expiresBeforeUtc,
            CancellationToken cancellationToken = default) => Task.FromResult(0);

        public Task<DrawingAssetMetadata?> GetByIdAsync(
            Guid assetId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Metadata.SingleOrDefault(asset => asset.Id == assetId));

        public Task<IReadOnlyList<DrawingAssetMetadata>> ListSubmissionAsync(
            Guid submissionId,
            Guid gameInstanceId,
            Guid playerId,
            string roundId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<DrawingAssetMetadata>>(Metadata
                .Where(asset => asset.SubmissionId == submissionId &&
                    asset.GameInstanceId == gameInstanceId &&
                    asset.PlayerId == playerId && asset.RoundId == roundId)
                .OrderBy(asset => asset.FrameNumber)
                .ToArray());

        public Task<bool> TryAddSubmissionAsync(
            IReadOnlyCollection<DrawingAssetMetadata> assets,
            CancellationToken cancellationToken = default)
        {
            var first = assets.First();
            if (Metadata.Any(asset => asset.SubmissionId == first.SubmissionId &&
                    asset.GameInstanceId == first.GameInstanceId &&
                    asset.PlayerId == first.PlayerId && asset.RoundId == first.RoundId))
            {
                return Task.FromResult(false);
            }
            Metadata.AddRange(assets);
            return Task.FromResult(true);
        }

        Task<int> IDrawingAssetMetadataRepository.DeleteExpiredAsync(
            DateTimeOffset expiresAtOrBeforeUtc,
            CancellationToken cancellationToken)
        {
            var removed = Metadata.RemoveAll(asset => asset.ExpiresAtUtc <= expiresAtOrBeforeUtc);
            return Task.FromResult(removed);
        }
    }
}
