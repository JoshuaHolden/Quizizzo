using Quizizzo.Application.Abstractions;
using Quizizzo.Application.Displays;
using Quizizzo.Domain.Displays;
using Quizizzo.Domain.Parties;

namespace Quizizzo.Application.Tests;

public sealed class DisplaySessionServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 26, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Restore_reuses_the_durable_session_without_exposing_its_hash()
    {
        var displays = new FakeDisplayRepository();
        var credentials = new FakeCredentials();
        var service = new DisplaySessionService(displays, new FakePartyRepository(), credentials, new FixedTimeProvider());

        var created = await service.RestoreOrCreateAsync(null);
        var restored = await service.RestoreOrCreateAsync(created.SessionToken);

        Assert.True(created.IsNew);
        Assert.False(restored.IsNew);
        Assert.Equal(created.View.DisplaySessionId, restored.View.DisplaySessionId);
        Assert.Equal("HASHED", displays.Sessions.Single().SessionTokenHash);
        Assert.DoesNotContain("raw-token", displays.Sessions.Single().SessionTokenHash);
    }

    [Fact]
    public async Task Pair_rejects_a_party_owned_by_another_host()
    {
        var displays = new FakeDisplayRepository();
        var parties = new FakePartyRepository();
        var party = Party.Create("host-1", RoomCode.Parse("K7XM"), Now);
        parties.Parties.Add(party);
        var display = DisplaySession.Create("HASH", "PAIRCODE", Now, TimeSpan.FromMinutes(15));
        displays.Sessions.Add(display);
        var service = new DisplaySessionService(displays, parties, new FakeCredentials(), new FixedTimeProvider());

        await Assert.ThrowsAsync<Quizizzo.Application.Parties.PartyAccessDeniedException>(
            () => service.PairAsync("PAIRCODE", party.Id.Value, "host-2"));
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class FakeCredentials : IDisplayCredentialService
    {
        public string GenerateSessionToken() => "raw-token";
        public string HashSessionToken(string sessionToken) => "HASHED";
        public string GeneratePairingCode() => "PAIRCODE";
    }

    private sealed class FakeDisplayRepository : IDisplaySessionRepository
    {
        public List<DisplaySession> Sessions { get; } = [];

        public Task<bool> PairingCodeExistsAsync(string pairingCode, CancellationToken cancellationToken) =>
            Task.FromResult(Sessions.Any(session => session.PairingCode == pairingCode));

        public Task AddAsync(DisplaySession displaySession, CancellationToken cancellationToken)
        {
            Sessions.Add(displaySession);
            return Task.CompletedTask;
        }

        public Task<DisplaySession?> GetBySessionTokenHashAsync(string tokenHash, CancellationToken cancellationToken) =>
            Task.FromResult(Sessions.SingleOrDefault(session => session.SessionTokenHash == tokenHash));

        public Task<DisplaySession?> GetByPairingCodeAsync(string pairingCode, CancellationToken cancellationToken) =>
            Task.FromResult(Sessions.SingleOrDefault(session => session.PairingCode == pairingCode));

        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FakePartyRepository : IPartyRepository
    {
        public List<Party> Parties { get; } = [];

        public Task<bool> ActiveRoomCodeExistsAsync(RoomCode roomCode, CancellationToken cancellationToken) =>
            Task.FromResult(Parties.Any(party => party.RoomCode == roomCode && party.HasActiveRoomCode));
        public Task AddAsync(Party party, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<Party?> GetByIdAsync(PartyId partyId, CancellationToken cancellationToken) =>
            Task.FromResult(Parties.SingleOrDefault(party => party.Id == partyId));
        public Task<Party?> GetActiveByHostAsync(string hostUserId, CancellationToken cancellationToken) =>
            Task.FromResult(Parties.SingleOrDefault(party => party.HostUserId == hostUserId && party.HasActiveRoomCode));
        public Task<IReadOnlyList<Party>> ListRecentByHostAsync(string hostUserId, int limit, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Party>>([]);
        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
