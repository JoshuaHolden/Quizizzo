using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Quizizzo.Application.Abstractions;
using Quizizzo.Application.Players;
using Quizizzo.Domain.Parties;
using Quizizzo.Domain.Players;
using Quizizzo.Web.Realtime;

namespace Quizizzo.IntegrationTests;

public sealed class RealtimePresenceTests
{
    [Fact]
    public async Task Multiple_connection_ids_represent_one_durable_player_identity()
    {
        var partyId = Guid.NewGuid();
        var notifier = new RecordingNotifier();
        using var services = new ServiceCollection().BuildServiceProvider();
        var registry = new PartyConnectionRegistry(
            services.GetRequiredService<IServiceScopeFactory>(),
            notifier,
            Options.Create(new RealtimePresenceOptions
            {
                PlayerDisconnectGracePeriod = TimeSpan.FromMilliseconds(25)
            }),
            NullLogger<PartyConnectionRegistry>.Instance);

        await registry.RegisterAsync("transport-a", partyId, RealtimeRole.Player, "durable-player", default);
        await registry.RegisterAsync("transport-b", partyId, RealtimeRole.Player, "durable-player", default);

        Assert.Equal(1, registry.GetSnapshot(partyId).Players);

        await registry.UnregisterAsync("transport-a");
        Assert.Equal(1, registry.GetSnapshot(partyId).Players);

        await registry.UnregisterAsync("transport-b");
        await registry.RegisterAsync("transport-c", partyId, RealtimeRole.Player, "durable-player", default);
        await Task.Delay(75);

        Assert.Equal(1, registry.GetSnapshot(partyId).Players);
        Assert.DoesNotContain(notifier.Events, item => item.Reason == "PlayerDisconnected");
    }

    [Fact]
    public async Task Presence_snapshot_counts_subjects_by_role_not_transports()
    {
        var partyId = Guid.NewGuid();
        using var services = new ServiceCollection().BuildServiceProvider();
        var registry = new PartyConnectionRegistry(
            services.GetRequiredService<IServiceScopeFactory>(),
            new RecordingNotifier(),
            Options.Create(new RealtimePresenceOptions()),
            NullLogger<PartyConnectionRegistry>.Instance);

        await registry.RegisterAsync("host-tab-1", partyId, RealtimeRole.Host, "host-user", default);
        await registry.RegisterAsync("host-tab-2", partyId, RealtimeRole.Host, "host-user", default);
        await registry.RegisterAsync("display-transport", partyId, RealtimeRole.Display, "display-session", default);

        Assert.Equal(new PartyPresenceSnapshot(1, 0, 1), registry.GetSnapshot(partyId));

        await registry.UnregisterAsync("host-tab-1");
        Assert.True(registry.GetSnapshot(partyId).HostConnected);
        await registry.UnregisterAsync("host-tab-2");
        Assert.False(registry.GetSnapshot(partyId).HostConnected);
    }

    [Fact]
    public void Group_names_are_stable_and_role_scoped()
    {
        var partyId = Guid.Parse("11111111-2222-3333-4444-555555555555");

        Assert.Equal("party:11111111222233334444555555555555", RealtimeGroups.Party(partyId));
        Assert.NotEqual(RealtimeGroups.Hosts(partyId), RealtimeGroups.Players(partyId));
        Assert.NotEqual(RealtimeGroups.Players(partyId), RealtimeGroups.Displays(partyId));
    }

    [Fact]
    public async Task Final_player_transport_marks_the_durable_player_disconnected_after_grace()
    {
        var now = DateTimeOffset.UtcNow;
        var party = Party.Create("host-user", RoomCode.Parse("K7XM"), now);
        var player = Player.Create(
            party.Id,
            PlayerName.Parse("Joshua"),
            new CharacterDefinition(
                CharacterBodyType.Round,
                "#4361EE",
                CharacterEyes.Starry,
                CharacterMouth.Grin,
                CharacterAccessory.PartyHat),
            "token-hash",
            now);
        var players = new PlayerRepositoryFake(player);
        var services = new ServiceCollection()
            .AddSingleton<IPartyRepository>(new PartyRepositoryFake(party))
            .AddSingleton<IPlayerRepository>(players)
            .AddSingleton<IPlayerCredentialService, UnusedPlayerCredentials>()
            .AddSingleton<ICharacterGenerator, UnusedCharacterGenerator>()
            .AddSingleton(TimeProvider.System)
            .AddScoped<PlayerService>()
            .BuildServiceProvider();
        await using var asyncServices = services;
        var notifier = new RecordingNotifier();
        var registry = new PartyConnectionRegistry(
            services.GetRequiredService<IServiceScopeFactory>(),
            notifier,
            Options.Create(new RealtimePresenceOptions
            {
                PlayerDisconnectGracePeriod = TimeSpan.FromMilliseconds(20)
            }),
            NullLogger<PartyConnectionRegistry>.Instance);

        await registry.RegisterAsync(
            "transport", party.Id.Value, RealtimeRole.Player, player.Id.Value.ToString(), default);
        await registry.UnregisterAsync("transport");

        for (var attempt = 0; attempt < 40 && player.Status != PlayerStatus.Disconnected; attempt++)
        {
            await Task.Delay(10);
        }

        Assert.Equal(PlayerStatus.Disconnected, player.Status);
        Assert.Contains(notifier.Events, item => item.Reason == "PlayerDisconnected");
    }

    private sealed class RecordingNotifier : IPartyRealtimeNotifier
    {
        public List<(Guid PartyId, string Reason)> Events { get; } = [];

        public Task PartyChangedAsync(Guid partyId, string reason, CancellationToken cancellationToken = default)
        {
            Events.Add((partyId, reason));
            return Task.CompletedTask;
        }

        public Task DisplaySessionChangedAsync(
            Guid displaySessionId,
            string reason,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class PlayerRepositoryFake(Player player) : IPlayerRepository
    {
        public Task<Player?> GetByIdAsync(PlayerId playerId, CancellationToken cancellationToken) =>
            Task.FromResult<Player?>(player.Id == playerId ? player : null);
        public Task<Player?> GetBySessionTokenHashAsync(string tokenHash, CancellationToken cancellationToken) =>
            Task.FromResult<Player?>(player.SessionTokenHash == tokenHash ? player : null);
        public Task<int> CountMembersAsync(PartyId partyId, CancellationToken cancellationToken) =>
            Task.FromResult(player.PartyId == partyId ? 1 : 0);
        public Task<IReadOnlyList<Player>> ListMembersAsync(PartyId partyId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Player>>(player.PartyId == partyId ? [player] : []);
        public Task AddAsync(Player addedPlayer, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class PartyRepositoryFake(Party party) : IPartyRepository
    {
        public Task<Party?> GetByIdAsync(PartyId partyId, CancellationToken cancellationToken) =>
            Task.FromResult<Party?>(party.Id == partyId ? party : null);
        public Task<Party?> GetByRoomCodeAsync(RoomCode roomCode, CancellationToken cancellationToken) =>
            Task.FromResult<Party?>(party.RoomCode == roomCode ? party : null);
        public Task<Party?> GetActiveByHostAsync(string hostUserId, CancellationToken cancellationToken) =>
            Task.FromResult<Party?>(party.HostUserId == hostUserId ? party : null);
        public Task<IReadOnlyList<Party>> ListRecentByHostAsync(
            string hostUserId, int limit, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Party>>([party]);
        public Task<bool> ActiveRoomCodeExistsAsync(RoomCode roomCode, CancellationToken cancellationToken) =>
            Task.FromResult(party.RoomCode == roomCode);
        public Task AddAsync(Party addedParty, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class UnusedPlayerCredentials : IPlayerCredentialService
    {
        public string GenerateSessionToken() => throw new NotSupportedException();
        public string HashSessionToken(string sessionToken) => throw new NotSupportedException();
    }

    private sealed class UnusedCharacterGenerator : ICharacterGenerator
    {
        public CharacterDefinition Generate() => throw new NotSupportedException();
    }
}
