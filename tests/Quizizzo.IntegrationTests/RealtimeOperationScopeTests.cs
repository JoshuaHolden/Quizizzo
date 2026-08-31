namespace Quizizzo.IntegrationTests;

public sealed class RealtimeOperationScopeTests
{
    [Fact]
    public void Realtime_display_loads_state_through_an_operation_scope()
    {
        var component = ReadRepositoryFile(
            "src/Quizizzo.Web/Components/Pages/DisplayRealtime.razor");
        var loader = ReadRepositoryFile(
            "src/Quizizzo.Web/Presentation/DisplayRealtimeStateLoader.cs");

        Assert.Contains("@inject DisplayRealtimeStateLoader StateLoader", component,
            StringComparison.Ordinal);
        Assert.DoesNotContain("@inject PartyGameService", component, StringComparison.Ordinal);
        Assert.DoesNotContain("@inject PlayerService", component, StringComparison.Ordinal);
        Assert.DoesNotContain("@inject DisplaySessionService", component, StringComparison.Ordinal);
        Assert.Contains("scopeFactory.CreateAsyncScope()", loader, StringComparison.Ordinal);
        Assert.Contains("await using var scope", loader, StringComparison.Ordinal);
    }

    [Fact]
    public void Realtime_host_and_player_do_not_capture_scoped_application_services()
    {
        var host = ReadRepositoryFile("src/Quizizzo.Web/Components/Pages/HostParty.razor");
        var player = ReadRepositoryFile("src/Quizizzo.Web/Components/Pages/PlayRealtime.razor");
        var hostService = ReadRepositoryFile(
            "src/Quizizzo.Web/Presentation/HostPartyRealtimeService.cs");
        var playerLoader = ReadRepositoryFile(
            "src/Quizizzo.Web/Presentation/PlayerRealtimeStateLoader.cs");

        Assert.Contains("@inject HostPartyRealtimeService PartyState", host,
            StringComparison.Ordinal);
        Assert.Contains("@inject PlayerRealtimeStateLoader StateLoader", player,
            StringComparison.Ordinal);
        Assert.DoesNotContain("@inject PartyGameService", host, StringComparison.Ordinal);
        Assert.DoesNotContain("@inject PartyGameService", player, StringComparison.Ordinal);
        Assert.Contains("scopeFactory.CreateAsyncScope()", hostService, StringComparison.Ordinal);
        Assert.Contains("CloseLobbyAsync", hostService, StringComparison.Ordinal);
        Assert.Contains("LobbyClosed", hostService, StringComparison.Ordinal);
        Assert.Contains("Close party", host, StringComparison.Ordinal);
        Assert.Contains("Yes, close party", host, StringComparison.Ordinal);
        Assert.Contains("scopeFactory.CreateAsyncScope()", playerLoader, StringComparison.Ordinal);
    }

    private static string ReadRepositoryFile(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "Quizizzo.sln")))
        {
            directory = directory.Parent;
        }

        if (directory is null)
        {
            throw new DirectoryNotFoundException("The Quizizzo repository root was not found.");
        }

        return File.ReadAllText(Path.Combine(directory.FullName, relativePath));
    }
}
