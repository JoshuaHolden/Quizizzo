using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Quizizzo.Web.Realtime;
using Quizizzo.GameEngine;
using Quizizzo.Games.SlopMachine;

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
        using var rigResponse = await client.GetAsync("/js/playerCharacterRig.js");
        using var bridgeResponse = await client.GetAsync("/js/phaserPresentation.js");

        phaserResponse.EnsureSuccessStatusCode();
        rigResponse.EnsureSuccessStatusCode();
        bridgeResponse.EnsureSuccessStatusCode();
        var phaser = await phaserResponse.Content.ReadAsStringAsync();
        var rig = await rigResponse.Content.ReadAsStringAsync();
        var bridge = await bridgeResponse.Content.ReadAsStringAsync();
        Assert.True(phaser.Length > 1_000_000);
        Assert.Contains("Phaser", phaser);
        Assert.Contains("window.quizizzoCharacterRig", rig);
        Assert.Contains("loadAtlases", rig);
        Assert.Contains("window.quizizzoPresentation", bridge);
        Assert.Contains("Phaser.Scale.ENVELOP", bridge);
        Assert.Contains("prefers-reduced-motion", bridge);
        Assert.DoesNotContain("signalR", bridge);
    }

    [Theory]
    [InlineData("/media/audio/quiz-show-groove.d6618b4f874d.mp3")]
    [InlineData("/media/audio/quiz-show-sparkle.774e332653a6.mp3")]
    [InlineData("/media/audio/countdown-to-zero.fd84e59f102d.mp3")]
    [InlineData("/media/audio/games/slop-machine/slop-lobby.mp3")]
    [InlineData("/media/audio/games/slop-machine/slop-writing.mp3")]
    [InlineData("/media/audio/games/slop-machine/slop-countdown.mp3")]
    [InlineData("/media/audio/games/slop-machine/slop-spinner.mp3")]
    [InlineData("/media/audio/games/slop-machine/slop-voting.mp3")]
    [InlineData("/media/audio/games/slop-machine/slop-telephone.mp3")]
    [InlineData("/media/audio/games/slop-machine/slop-comments.mp3")]
    [InlineData("/media/audio/games/slop-machine/slop-scoreboard.mp3")]
    [InlineData("/media/audio/games/slop-machine/slop-final.mp3")]
    [InlineData("/media/audio/games/slop-machine/slop-human-victory.mp3")]
    [InlineData("/media/audio/games/slop-machine/slop-machine-victory.mp3")]
    public async Task Presentation_audio_is_served_with_long_lived_edge_cache_headers(string path)
    {
        using var response = await client.GetAsync(path);

        response.EnsureSuccessStatusCode();
        Assert.Equal("audio/mpeg", response.Content.Headers.ContentType?.MediaType);
        Assert.True(response.Content.Headers.ContentLength > 300_000);
        Assert.True(response.Headers.CacheControl?.Public);
        Assert.Equal(TimeSpan.FromDays(365), response.Headers.CacheControl?.MaxAge);
        Assert.Contains("immutable", response.Headers.CacheControl?.Extensions.Select(value => value.Name) ?? []);
        Assert.Equal("public, max-age=31536000", response.Headers.GetValues("CDN-Cache-Control").Single());
    }

    [Fact]
    public async Task Slop_machine_thumbnail_is_served_as_webp_with_edge_cache_headers()
    {
        using var response = await client.GetAsync(
            "/media/games/slop-machine/thumbnails/cb-000001.webp");

        response.EnsureSuccessStatusCode();
        Assert.Equal("image/webp", response.Content.Headers.ContentType?.MediaType);
        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.True(bytes.Length > 10_000);
        Assert.Equal("RIFF", System.Text.Encoding.ASCII.GetString(bytes, 0, 4));
        Assert.Equal("WEBP", System.Text.Encoding.ASCII.GetString(bytes, 8, 4));
        Assert.True(response.Headers.CacheControl?.Public);
        Assert.Equal(TimeSpan.FromDays(365), response.Headers.CacheControl?.MaxAge);
        Assert.Contains("immutable", response.Headers.CacheControl?.Extensions.Select(value => value.Name) ?? []);
        Assert.Equal("public, max-age=31536000",
            response.Headers.GetValues("CDN-Cache-Control").Single());
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
        var rigIndex = html.IndexOf("src=\"js/playerCharacterRig", StringComparison.Ordinal);
        var audioIndex = html.IndexOf("src=\"js/presentationAudio", StringComparison.Ordinal);
        var bridgeIndex = html.IndexOf("src=\"js/phaserPresentation", StringComparison.Ordinal);
        var blazorIndex = html.IndexOf("src=\"_framework/blazor.web", StringComparison.Ordinal);

        Assert.True(phaserIndex >= 0);
        Assert.True(rigIndex > phaserIndex);
        Assert.True(audioIndex > rigIndex);
        Assert.True(bridgeIndex > audioIndex);
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

    [Fact]
    public void Production_catalog_discovers_slop_machine_with_platform_player_limits()
    {
        var descriptor = factory.Services.GetRequiredService<GameModuleCatalog>()
            .List().Single(item => item.Key == SlopMachineGameModule.GameKey);

        Assert.Equal("Slop Machine", descriptor.DisplayName);
        Assert.Equal(2, descriptor.MinimumPlayers);
        Assert.Equal(12, descriptor.MaximumPlayers);
    }
}
