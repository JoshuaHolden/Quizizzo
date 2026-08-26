using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Quizizzo.Infrastructure.Identity;
using Quizizzo.Domain.Displays;
using Quizizzo.Domain.Parties;
using Quizizzo.Domain.Players;
using Quizizzo.Web.Presentation;

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
    }

    [Fact]
    public void ApplicationDbContext_uses_the_PostgreSQL_provider()
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        Assert.Equal("Npgsql.EntityFrameworkCore.PostgreSQL", dbContext.Database.ProviderName);
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
    }

    [Fact]
    public async Task Host_dashboard_requires_authentication()
    {
        using var response = await client.GetAsync("/host");

        var responseBody = await response.Content.ReadAsStringAsync();
        Assert.True(
            response.StatusCode == System.Net.HttpStatusCode.Redirect,
            $"Expected redirect but received {response.StatusCode}: {responseBody}");
        Assert.Equal("/Account/Login", response.Headers.Location?.AbsolutePath);
    }

    [Fact]
    public void Qr_code_service_generates_a_png_data_uri()
    {
        var qrCodes = new QrCodeService();

        var dataUri = qrCodes.CreatePngDataUri("https://quizizzo.example/join/K7XM");

        Assert.StartsWith("data:image/png;base64,", dataUri);
        Assert.True(Convert.FromBase64String(dataUri.Split(',')[1]).Length > 100);
    }
}
