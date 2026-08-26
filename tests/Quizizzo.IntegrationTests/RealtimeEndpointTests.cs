using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Quizizzo.Web.Realtime;

namespace Quizizzo.IntegrationTests;

public sealed class RealtimeEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> factory;
    private readonly HttpClient client;

    public RealtimeEndpointTests(WebApplicationFactory<Program> factory)
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
    public async Task Party_hub_exposes_the_standard_negotiate_endpoint()
    {
        using var content = new StringContent(string.Empty);
        using var response = await client.PostAsync("/hubs/party/negotiate?negotiateVersion=1", content);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("connectionToken", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task SignalR_browser_client_is_served_locally()
    {
        using var response = await client.GetAsync("/vendor/signalr.min.js");

        response.EnsureSuccessStatusCode();
        Assert.Contains("HubConnectionBuilder", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public void Realtime_services_use_one_process_wide_presence_registry()
    {
        var first = factory.Services.GetRequiredService<PartyConnectionRegistry>();
        var second = factory.Services.GetRequiredService<PartyConnectionRegistry>();

        Assert.Same(first, second);
        Assert.IsType<SignalRPartyRealtimeNotifier>(
            factory.Services.GetRequiredService<IPartyRealtimeNotifier>());
    }
}
