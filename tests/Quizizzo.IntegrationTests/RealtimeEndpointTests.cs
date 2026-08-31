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
    public async Task Phaser_runtime_and_semantic_presentation_bridge_are_served_locally()
    {
        using var phaserResponse = await client.GetAsync("/vendor/phaser.min.js");
        using var bridgeResponse = await client.GetAsync("/js/phaserPresentation.js");

        phaserResponse.EnsureSuccessStatusCode();
        bridgeResponse.EnsureSuccessStatusCode();
        var phaser = await phaserResponse.Content.ReadAsStringAsync();
        var bridge = await bridgeResponse.Content.ReadAsStringAsync();
        Assert.True(phaser.Length > 1_000_000);
        Assert.Contains("Phaser", phaser);
        Assert.Contains("window.quizizzoPresentation", bridge);
        Assert.Contains("Phaser.Scale.FIT", bridge);
        Assert.Contains("prefers-reduced-motion", bridge);
        Assert.DoesNotContain("signalR", bridge);
    }

    [Fact]
    public async Task Presentation_fonts_are_served_locally()
    {
        using var displayFont = await client.GetAsync("/fonts/fredoka-700.woff2");
        using var bodyFont = await client.GetAsync("/fonts/nunito-600.woff2");

        displayFont.EnsureSuccessStatusCode();
        bodyFont.EnsureSuccessStatusCode();
        Assert.Equal("font/woff2", displayFont.Content.Headers.ContentType?.MediaType);
        Assert.Equal("font/woff2", bodyFont.Content.Headers.ContentType?.MediaType);
        Assert.True((await displayFont.Content.ReadAsByteArrayAsync()).Length > 10_000);
        Assert.True((await bodyFont.Content.ReadAsByteArrayAsync()).Length > 10_000);
    }

    [Fact]
    public async Task Browser_shell_loads_Phaser_before_the_presentation_bridge_and_Blazor()
    {
        using var response = await client.GetAsync("/");

        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync();
        var phaserIndex = html.IndexOf("src=\"vendor/phaser.min", StringComparison.Ordinal);
        var bridgeIndex = html.IndexOf("src=\"js/phaserPresentation", StringComparison.Ordinal);
        var blazorIndex = html.IndexOf("src=\"_framework/blazor.web", StringComparison.Ordinal);

        Assert.True(phaserIndex >= 0);
        Assert.True(bridgeIndex > phaserIndex);
        Assert.True(blazorIndex > bridgeIndex);
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
