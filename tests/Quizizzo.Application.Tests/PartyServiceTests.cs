using Quizizzo.Application.Abstractions;
using Quizizzo.Application.Parties;
using Quizizzo.Domain.Parties;

namespace Quizizzo.Application.Tests;

public sealed class PartyServiceTests
{
    [Fact]
    public async Task Create_retries_a_colliding_room_code()
    {
        var repository = new FakePartyRepository();
        repository.Parties.Add(Party.Create("other-host", RoomCode.Parse("K7XM"), DateTimeOffset.UtcNow));
        var generator = new SequenceRoomCodeGenerator("K7XM", "B4TP");
        var service = new PartyService(repository, generator, new FixedTimeProvider());

        var created = await service.CreateAsync("host-1");

        Assert.Equal("B4TP", created.RoomCode);
        Assert.Equal("host-1", repository.Parties.Single(party => party.Id.Value == created.PartyId).HostUserId);
        Assert.Equal(2, generator.Calls);
    }

    [Fact]
    public async Task GetOwned_rejects_a_different_host()
    {
        var repository = new FakePartyRepository();
        var party = Party.Create("host-1", RoomCode.Parse("K7XM"), DateTimeOffset.UtcNow);
        repository.Parties.Add(party);
        var service = new PartyService(repository, new SequenceRoomCodeGenerator("B4TP"), new FixedTimeProvider());

        await Assert.ThrowsAsync<PartyAccessDeniedException>(
            () => service.GetOwnedAsync(party.Id.Value, "host-2"));
    }

    [Fact]
    public async Task Create_rejects_a_second_active_party_for_the_same_host()
    {
        var repository = new FakePartyRepository();
        repository.Parties.Add(Party.Create("host-1", RoomCode.Parse("K7XM"), DateTimeOffset.UtcNow));
        var service = new PartyService(repository, new SequenceRoomCodeGenerator("B4TP"), new FixedTimeProvider());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.CreateAsync("host-1"));

        Assert.Contains("active party", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 8, 26, 12, 0, 0, TimeSpan.Zero);
    }

    private sealed class SequenceRoomCodeGenerator(params string[] codes) : IRoomCodeGenerator
    {
        private readonly Queue<string> codes = new(codes);
        public int Calls { get; private set; }

        public RoomCode Generate()
        {
            Calls++;
            return RoomCode.Parse(codes.Dequeue());
        }
    }

    private sealed class FakePartyRepository : IPartyRepository
    {
        public List<Party> Parties { get; } = [];

        public Task<bool> ActiveRoomCodeExistsAsync(RoomCode roomCode, CancellationToken cancellationToken) =>
            Task.FromResult(Parties.Any(party => party.RoomCode == roomCode && party.HasActiveRoomCode));

        public Task AddAsync(Party party, CancellationToken cancellationToken)
        {
            Parties.Add(party);
            return Task.CompletedTask;
        }

        public Task<Party?> GetByIdAsync(PartyId partyId, CancellationToken cancellationToken) =>
            Task.FromResult(Parties.SingleOrDefault(party => party.Id == partyId));

        public Task<Party?> GetActiveByHostAsync(string hostUserId, CancellationToken cancellationToken) =>
            Task.FromResult(Parties.LastOrDefault(party => party.HostUserId == hostUserId && party.HasActiveRoomCode));

        public Task<IReadOnlyList<Party>> ListRecentByHostAsync(string hostUserId, int limit, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Party>>(Parties.Where(party => party.HostUserId == hostUserId).Take(limit).ToArray());

        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
