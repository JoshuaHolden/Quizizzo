using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Quizizzo.Domain.Players;
using Quizizzo.Web.Endpoints;
using Quizizzo.Web.Realtime;

namespace Quizizzo.IntegrationTests;

public sealed class RoleRecoveryTests
{
    [Fact]
    public async Task Host_refresh_and_connection_replacement_preserve_owner_identity()
    {
        await using var factory = new RecoveryWebApplicationFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(
            RecoveryWebApplicationFactory.HostHeader,
            RecoveryWebApplicationFactory.HostUserId);
        var partyPath = $"/host/party/{factory.State.Party.Id.Value}";

        var firstPage = await GetPageAsync(client, partyPath);
        var refreshedPage = await GetPageAsync(client, partyPath);

        Assert.Contains("K7XM", firstPage);
        Assert.Contains("K7XM", refreshedPage);
        Assert.Equal(RecoveryWebApplicationFactory.HostUserId, factory.State.Party.HostUserId);

        await using var original = CreateConnection(factory, hostUserId: RecoveryWebApplicationFactory.HostUserId);
        await using var replacement = CreateConnection(factory, hostUserId: RecoveryWebApplicationFactory.HostUserId);
        await original.StartAsync();
        await original.InvokeAsync("ConnectHost", factory.State.Party.Id.Value);
        await replacement.StartAsync();
        await replacement.InvokeAsync("ConnectHost", factory.State.Party.Id.Value);

        Assert.NotEqual(original.ConnectionId, replacement.ConnectionId);
        Assert.Equal(1, GetPresence(factory).Hosts);

        await original.StopAsync();
        Assert.Equal(1, GetPresence(factory).Hosts);
        await replacement.StopAsync();
        await WaitUntilAsync(() => GetPresence(factory).Hosts == 0);
        Assert.Equal(0, GetPresence(factory).Hosts);
    }

    [Fact]
    public async Task Display_refresh_and_connection_replacement_preserve_display_session()
    {
        await using var factory = new RecoveryWebApplicationFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(
            "Cookie",
            $"quizizzo.display={RecoveryWebApplicationFactory.DisplayToken}");
        var displaySessionId = factory.State.Display.Id;

        var firstPage = await GetPageAsync(client, "/display");
        var refreshedPage = await GetPageAsync(client, "/display");

        Assert.Contains("K7XM", firstPage);
        Assert.Contains("Recovery Player", firstPage);
        Assert.Contains("phaser-presentation", firstPage);
        Assert.Contains("K7XM", refreshedPage);
        Assert.Equal(displaySessionId, factory.State.Display.Id);

        await using var original = CreateConnection(
            factory,
            cookie: $"quizizzo.display={RecoveryWebApplicationFactory.DisplayToken}");
        await using var replacement = CreateConnection(
            factory,
            cookie: $"quizizzo.display={RecoveryWebApplicationFactory.DisplayToken}");
        await original.StartAsync();
        await original.InvokeAsync("ConnectDisplay");
        await replacement.StartAsync();
        await replacement.InvokeAsync("ConnectDisplay");

        Assert.NotEqual(original.ConnectionId, replacement.ConnectionId);
        Assert.Equal(1, GetPresence(factory).Displays);

        await original.StopAsync();
        Assert.Equal(1, GetPresence(factory).Displays);
        await replacement.StopAsync();
        await WaitUntilAsync(() => GetPresence(factory).Displays == 0);
        Assert.Equal(0, GetPresence(factory).Displays);
        Assert.Equal(displaySessionId, factory.State.Display.Id);
    }

    [Fact]
    public async Task Player_refresh_connection_replacement_and_grace_preserve_player_identity()
    {
        await using var factory = new RecoveryWebApplicationFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(
            "Cookie",
            $"{PlayerSessionEndpoints.PlayerCookieName}={RecoveryWebApplicationFactory.PlayerToken}");
        var playerId = factory.State.Player.Id;

        var firstPage = await GetPageAsync(client, "/play");
        var refreshedPage = await GetPageAsync(client, "/play");

        Assert.Contains("Recovery Player", firstPage);
        Assert.Contains("Recovery Player", refreshedPage);
        Assert.Equal(playerId, factory.State.Player.Id);

        await using var original = CreateConnection(
            factory,
            cookie: $"{PlayerSessionEndpoints.PlayerCookieName}={RecoveryWebApplicationFactory.PlayerToken}");
        await using var replacement = CreateConnection(
            factory,
            cookie: $"{PlayerSessionEndpoints.PlayerCookieName}={RecoveryWebApplicationFactory.PlayerToken}");
        await original.StartAsync();
        await original.InvokeAsync("ConnectPlayer");
        await replacement.StartAsync();
        await replacement.InvokeAsync("ConnectPlayer");

        Assert.NotEqual(original.ConnectionId, replacement.ConnectionId);
        Assert.Equal(1, GetPresence(factory).Players);

        await original.StopAsync();
        Assert.Equal(1, GetPresence(factory).Players);
        await replacement.StopAsync();

        await using var withinGrace = CreateConnection(
            factory,
            cookie: $"{PlayerSessionEndpoints.PlayerCookieName}={RecoveryWebApplicationFactory.PlayerToken}");
        await withinGrace.StartAsync();
        await withinGrace.InvokeAsync("ConnectPlayer");
        await Task.Delay(350);

        Assert.Equal(playerId, factory.State.Player.Id);
        Assert.Equal(PlayerStatus.Connected, factory.State.Player.Status);
        Assert.Equal(1, GetPresence(factory).Players);

        await withinGrace.StopAsync();
        await WaitUntilAsync(() => factory.State.Player.Status == PlayerStatus.Disconnected);

        var recoveredPage = await GetPageAsync(client, "/play");
        Assert.Contains("Recovery Player", recoveredPage);
        Assert.Equal(playerId, factory.State.Player.Id);
        Assert.Equal(PlayerStatus.Connected, factory.State.Player.Status);
    }

    private static HubConnection CreateConnection(
        RecoveryWebApplicationFactory factory,
        string? cookie = null,
        string? hostUserId = null)
    {
        return new HubConnectionBuilder()
            .WithUrl(new Uri(factory.Server.BaseAddress, "/hubs/party"), options =>
            {
                options.Transports = HttpTransportType.LongPolling;
                options.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
                if (cookie is not null)
                {
                    options.Headers.Add("Cookie", cookie);
                }
                if (hostUserId is not null)
                {
                    options.Headers.Add(RecoveryWebApplicationFactory.HostHeader, hostUserId);
                }
            })
            .Build();
    }

    private static PartyPresenceSnapshot GetPresence(RecoveryWebApplicationFactory factory) =>
        factory.Services.GetRequiredService<PartyConnectionRegistry>()
            .GetSnapshot(factory.State.Party.Id.Value);

    private static async Task<string> GetPageAsync(HttpClient client, string path)
    {
        using var response = await client.GetAsync(path);
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"GET {path} returned {(int)response.StatusCode}: {body}");
        return body;
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 100 && !condition(); attempt++)
        {
            await Task.Delay(10);
        }

        Assert.True(condition(), "The expected recovery state was not reached before the timeout.");
    }
}
