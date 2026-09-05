using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Quizizzo.Infrastructure.Identity;
using Quizizzo.Domain.Displays;
using Quizizzo.Domain.Parties;
using Quizizzo.Domain.Players;
using Quizizzo.Web.Presentation;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Quizizzo.Infrastructure.Drawings;
using Quizizzo.Domain.Drawings;
using Quizizzo.Infrastructure.Games;

namespace Quizizzo.IntegrationTests;

public sealed class HealthEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient client;
    private readonly WebApplicationFactory<Program> factory;

    public HealthEndpointTests(WebApplicationFactory<Program> factory)
    {
        this.factory = factory.WithWebHostBuilder(builder =>
            builder.ConfigureLogging(logging => logging.ClearProviders()));
        client = this.factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost")
        });
    }

    [Fact]
    public async Task Live_endpoint_reports_success_without_requiring_the_database()
    {
        using var response = await client.GetAsync("/health/live");

        response.EnsureSuccessStatusCode();
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
        Assert.Equal(
            "camera=(), microphone=(self), geolocation=()",
            response.Headers.GetValues("Permissions-Policy").Single());
    }

    [Theory]
    [InlineData("/js/drawingDocument.mjs", "class DrawingDocument")]
    [InlineData("/js/drawingCanvas.js", "pointerdown")]
    public async Task Drawing_runtime_is_served_locally(string path, string expectedSource)
    {
        using var response = await client.GetAsync(path);

        response.EnsureSuccessStatusCode();
        Assert.Contains(expectedSource, await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public void ApplicationDbContext_uses_the_PostgreSQL_provider()
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var connectionSettings = new NpgsqlConnectionStringBuilder(
            dbContext.Database.GetConnectionString());

        Assert.Equal("Npgsql.EntityFrameworkCore.PostgreSQL", dbContext.Database.ProviderName);
        Assert.Equal(32, connectionSettings.MaxPoolSize);
        Assert.Equal(60, connectionSettings.ConnectionIdleLifetime);
    }

    [Fact]
    public void Persistence_model_enforces_active_room_and_display_session_uniqueness()
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var party = dbContext.Model.FindEntityType(typeof(Party))!;
        var roomCodeIndex = party.GetIndexes().Single(index =>
            index.Properties.Select(property => property.Name).SequenceEqual([nameof(Party.RoomCode)]));
        var activeHostIndex = party.GetIndexes().Single(index =>
            index.Properties.Select(property => property.Name).SequenceEqual([nameof(Party.HostUserId)]));
        var display = dbContext.Model.FindEntityType(typeof(DisplaySession))!;
        var player = dbContext.Model.FindEntityType(typeof(Player))!;
        var currentGameKey = party.FindProperty(nameof(Party.CurrentGameKey))!;
        var currentGameInstanceId = party.FindProperty(nameof(Party.CurrentGameInstanceId))!;

        Assert.True(roomCodeIndex.IsUnique);
        Assert.Equal(
            "\"Status\" IN (0, 1, 2, 3)",
            roomCodeIndex.FindAnnotation(RelationalAnnotationNames.Filter)?.Value);
        Assert.True(activeHostIndex.IsUnique);
        Assert.Equal(
            "\"Status\" IN (0, 1, 2, 3)",
            activeHostIndex.FindAnnotation(RelationalAnnotationNames.Filter)?.Value);
        Assert.Contains(display.GetIndexes(), index =>
            index.IsUnique && index.Properties.Single().Name == nameof(DisplaySession.SessionTokenHash));
        Assert.Contains(display.GetIndexes(), index =>
            index.IsUnique && index.Properties.Single().Name == nameof(DisplaySession.PairingCode));
        Assert.Contains(player.GetIndexes(), index =>
            index.IsUnique && index.Properties.Single().Name == nameof(Player.SessionTokenHash));
        Assert.Contains(player.GetIndexes(), index =>
            index.Properties.Select(property => property.Name)
                .SequenceEqual([nameof(Player.PartyId), nameof(Player.Status)]));
        var gameWin = dbContext.Model.FindEntityType(typeof(PlayerGameWin))!;
        Assert.Equal("PlayerGameWins", gameWin.GetTableName());
        Assert.Equal(
            ["PlayerId", nameof(PlayerGameWin.GameInstanceId)],
            gameWin.FindPrimaryKey()!.Properties.Select(property => property.Name));
        Assert.Equal(64, gameWin.FindProperty(nameof(PlayerGameWin.GameKey))!.GetMaxLength());
        Assert.Equal(64, currentGameKey.GetMaxLength());
        Assert.True(currentGameKey.IsNullable);
        Assert.True(currentGameInstanceId.IsNullable);
        Assert.Equal("jsonb", party.FindProperty(nameof(Party.GameQueue))!.GetColumnType());
        Assert.Contains(
            "20260903090000_AddPartyGameQueue",
            dbContext.Database.GetMigrations());
        var drawingAsset = dbContext.Model.FindEntityType(typeof(DrawingAssetMetadata))!;
        Assert.Contains(drawingAsset.GetIndexes(), index =>
            index.Properties.Single().Name == nameof(DrawingAssetMetadata.ExpiresAtUtc));
        Assert.Contains(drawingAsset.GetIndexes(), index => index.IsUnique &&
            index.Properties.Select(property => property.Name).SequenceEqual([
                nameof(DrawingAssetMetadata.SubmissionId),
                nameof(DrawingAssetMetadata.GameInstanceId),
                nameof(DrawingAssetMetadata.PlayerId),
                nameof(DrawingAssetMetadata.RoundId),
                nameof(DrawingAssetMetadata.FrameNumber)]));
        var gameSnapshot = dbContext.Model.GetEntityTypes().Single(entity =>
            entity.GetTableName() == "GameRuntimeSnapshots");
        Assert.Equal("jsonb", gameSnapshot.FindProperty("SnapshotJson")!.GetColumnType());
        Assert.Contains(gameSnapshot.GetIndexes(), index =>
            index.Properties.Select(property => property.Name)
                .SequenceEqual(["IsComplete", "UpdatedAtUtc"]));
    }

    [Fact]
    public void Qr_code_service_generates_a_png_data_uri()
    {
        var qrCodes = new QrCodeService();

        var dataUri = qrCodes.CreatePngDataUri("https://quizizzo.example/join/K7XM");

        Assert.StartsWith("data:image/png;base64,", dataUri);
        Assert.True(Convert.FromBase64String(dataUri.Split(',')[1]).Length > 100);
    }

    [Fact]
    public void Bounded_storage_cleanup_workers_are_registered()
    {
        var hostedServices = factory.Services.GetServices<IHostedService>();

        Assert.Contains(hostedServices, service => service is DrawingAssetCleanupService);
        Assert.Contains(hostedServices, service => service is GameSnapshotCleanupService);
    }

    [Fact]
    public void Drawing_image_validator_requires_a_complete_512_square_png()
    {
        var png = PngTestData.Create();

        Assert.True(Quizizzo.Web.Drawing.DrawingImageValidator.IsPngWithDimensions(png, 512, 512));
        Assert.False(Quizizzo.Web.Drawing.DrawingImageValidator.IsPngWithDimensions(png, 256, 512));
        Assert.False(Quizizzo.Web.Drawing.DrawingImageValidator.IsPngWithDimensions(
            png.AsSpan(0, png.Length - 12), 512, 512));
        png[0] = 0;
        Assert.False(Quizizzo.Web.Drawing.DrawingImageValidator.IsPngWithDimensions(png, 512, 512));
    }
}
