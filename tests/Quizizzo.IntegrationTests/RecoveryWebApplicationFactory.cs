using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Quizizzo.Application.Abstractions;
using Quizizzo.Domain.Displays;
using Quizizzo.Domain.Parties;
using Quizizzo.Domain.Players;
using Quizizzo.Web.Realtime;
using Quizizzo.GameEngine;

namespace Quizizzo.IntegrationTests;

internal sealed class RecoveryWebApplicationFactory : WebApplicationFactory<Program>
{
    public const string HostUserId = "recovery-host";
    public const string HostHeader = "X-Quizizzo-Test-Host";
    public const string PlayerToken = "durable-player-token";
    public const string DisplayToken = "durable-display-token";

    public RecoveryState State { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureLogging(logging => logging.ClearProviders());
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IPartyRepository>();
            services.RemoveAll<IPlayerRepository>();
            services.RemoveAll<IDisplaySessionRepository>();
            services.RemoveAll<IPlayerCredentialService>();
            services.RemoveAll<IDisplayCredentialService>();
            services.RemoveAll<IGameStateStore>();

            services.AddDataProtection().UseEphemeralDataProtectionProvider();
            services.AddSingleton(State);
            services.AddSingleton<IPartyRepository>(State);
            services.AddSingleton<IPlayerRepository>(State);
            services.AddSingleton<IDisplaySessionRepository>(State);
            services.AddSingleton<IPlayerCredentialService, RecoveryPlayerCredentials>();
            services.AddSingleton<IDisplayCredentialService, RecoveryDisplayCredentials>();
            services.AddSingleton<IGameStateStore, InMemoryGameStateStore>();
            services.Configure<RealtimePresenceOptions>(options =>
                options.PlayerDisconnectGracePeriod = TimeSpan.FromMilliseconds(250));
            services.AddAuthentication(RecoveryAuthenticationHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, RecoveryAuthenticationHandler>(
                    RecoveryAuthenticationHandler.SchemeName,
                    _ => { });
        });
    }

    internal sealed class RecoveryState : IPartyRepository, IPlayerRepository, IDisplaySessionRepository
    {
        private readonly object gate = new();
        private readonly List<DisplaySession> displays = [];

        public RecoveryState()
        {
            var now = DateTimeOffset.UtcNow;
            Party = Party.Create(HostUserId, RoomCode.Parse("K7XM"), now);
            Player = Player.Create(
                Party.Id,
                PlayerName.Parse("Recovery Player"),
                new CharacterDefinition(
                    CharacterBodyType.Round,
                    "#4361EE",
                    CharacterEyes.Starry,
                    CharacterMouth.Grin,
                    CharacterAccessory.PartyHat),
                RecoveryPlayerCredentials.Hash(PlayerToken),
                now);
            OtherPlayer = Player.Create(
                Party.Id,
                PlayerName.Parse("Second Player"),
                new CharacterDefinition(
                    CharacterBodyType.Bean,
                    "#F97316",
                    CharacterEyes.Googly,
                    CharacterMouth.Smile,
                    CharacterAccessory.BowTie),
                RecoveryPlayerCredentials.Hash("second-player-token"),
                now.AddSeconds(1));
            Display = DisplaySession.Create(
                RecoveryDisplayCredentials.Hash(DisplayToken),
                "RECOVER1",
                now,
                TimeSpan.FromMinutes(15));
            Display.Pair(Party, HostUserId, now);
            displays.Add(Display);
        }

        public Party Party { get; }
        public Player Player { get; }
        public Player OtherPlayer { get; }
        public DisplaySession Display { get; }
        public IReadOnlyList<DisplaySession> Displays
        {
            get
            {
                lock (gate)
                {
                    return displays.ToArray();
                }
            }
        }

        public Task<bool> ActiveRoomCodeExistsAsync(RoomCode roomCode, CancellationToken cancellationToken) =>
            Task.FromResult(Party.RoomCode == roomCode && Party.HasActiveRoomCode);

        public Task AddAsync(Party party, CancellationToken cancellationToken) =>
            throw new NotSupportedException("The recovery fixture is pre-seeded.");

        public Task<Party?> GetByIdAsync(PartyId partyId, CancellationToken cancellationToken) =>
            Task.FromResult<Party?>(Party.Id == partyId ? Party : null);

        public Task<Party?> GetByRoomCodeAsync(RoomCode roomCode, CancellationToken cancellationToken) =>
            Task.FromResult<Party?>(Party.RoomCode == roomCode ? Party : null);

        public Task<Party?> GetActiveByHostAsync(string hostUserId, CancellationToken cancellationToken) =>
            Task.FromResult<Party?>(Party.HostUserId == hostUserId && Party.HasActiveRoomCode ? Party : null);

        public Task<IReadOnlyList<Party>> ListRecentByHostAsync(
            string hostUserId,
            int limit,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Party>>(Party.HostUserId == hostUserId ? [Party] : []);

        public Task<int> CountMembersAsync(PartyId partyId, CancellationToken cancellationToken) =>
            Task.FromResult(Player.PartyId == partyId && Player.IsPartyMember ? 2 : 0);

        public Task AddAsync(Player player, CancellationToken cancellationToken) =>
            throw new NotSupportedException("The recovery fixture is pre-seeded.");

        public Task<Player?> GetByIdAsync(PlayerId playerId, CancellationToken cancellationToken) =>
            Task.FromResult<Player?>(Player.Id == playerId
                ? Player
                : OtherPlayer.Id == playerId ? OtherPlayer : null);

        Task<Player?> IPlayerRepository.GetBySessionTokenHashAsync(
            string tokenHash,
            CancellationToken cancellationToken) =>
            Task.FromResult<Player?>(Player.SessionTokenHash == tokenHash ? Player : null);

        public Task<IReadOnlyList<Player>> ListMembersAsync(PartyId partyId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Player>>(
                Player.PartyId == partyId && Player.IsPartyMember ? [Player, OtherPlayer] : []);

        public Task<bool> PairingCodeExistsAsync(string pairingCode, CancellationToken cancellationToken) =>
            Task.FromResult(Displays.Any(display => display.PairingCode == pairingCode));

        public Task AddAsync(DisplaySession displaySession, CancellationToken cancellationToken)
        {
            lock (gate)
            {
                displays.Add(displaySession);
            }

            return Task.CompletedTask;
        }

        public Task<DisplaySession?> GetByIdAsync(
            DisplaySessionId displaySessionId,
            CancellationToken cancellationToken) =>
            Task.FromResult(Displays.FirstOrDefault(display => display.Id == displaySessionId));

        Task<DisplaySession?> IDisplaySessionRepository.GetBySessionTokenHashAsync(
            string tokenHash,
            CancellationToken cancellationToken)
        {
            lock (gate)
            {
                return Task.FromResult(displays.FirstOrDefault(display => display.SessionTokenHash == tokenHash));
            }
        }

        public Task<DisplaySession?> GetByPairingCodeAsync(
            string pairingCode,
            CancellationToken cancellationToken) =>
            Task.FromResult(Displays.FirstOrDefault(display => display.PairingCode == pairingCode));

        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class RecoveryPlayerCredentials : IPlayerCredentialService
    {
        public static string Hash(string token) => $"player-hash:{token}";
        public string GenerateSessionToken() => throw new NotSupportedException();
        public string HashSessionToken(string sessionToken) => Hash(sessionToken);
    }

    private sealed class RecoveryDisplayCredentials : IDisplayCredentialService
    {
        public static string Hash(string token) => $"display-hash:{token}";
        public string GenerateSessionToken() => "generated-display-token";
        public string HashSessionToken(string sessionToken) => Hash(sessionToken);
        public string GeneratePairingCode() => "GENERATE1";
    }

    private sealed class RecoveryAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "QuizizzoRecoveryTest";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue(HostHeader, out var hostUserId) ||
                string.IsNullOrWhiteSpace(hostUserId))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var identity = new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, hostUserId.ToString())],
                SchemeName);
            var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }
}
