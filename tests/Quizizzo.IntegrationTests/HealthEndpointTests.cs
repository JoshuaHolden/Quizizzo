using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Quizizzo.Infrastructure.Identity;
using Quizizzo.Domain.Displays;
using Quizizzo.Domain.Parties;

namespace Quizizzo.IntegrationTests;

public sealed class HealthEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient client;
    private readonly WebApplicationFactory<Program> factory;

    public HealthEndpointTests(WebApplicationFactory<Program> factory)
    {
        this.factory = factory;
        client = factory.CreateClient(new WebApplicationFactoryClientOptions
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
    }

    [Fact]
    public async Task Host_dashboard_requires_authentication()
    {
        using var response = await client.GetAsync("/host");

        Assert.Equal(System.Net.HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Account/Login", response.Headers.Location?.AbsolutePath);
    }
}
