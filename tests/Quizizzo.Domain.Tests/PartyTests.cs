using Quizizzo.Domain.Parties;

namespace Quizizzo.Domain.Tests;

public sealed class PartyTests
{
    [Fact]
    public void New_party_opens_in_lobby_and_retains_owner()
    {
        var createdAt = new DateTimeOffset(2026, 8, 26, 12, 0, 0, TimeSpan.Zero);

        var party = Party.Create("host-1", RoomCode.Parse("K7XM"), createdAt);

        Assert.Equal(PartyStatus.Lobby, party.Status);
        Assert.Equal(createdAt, party.CreatedAt);
        Assert.True(party.IsOwnedBy("host-1"));
        Assert.False(party.IsOwnedBy("host-2"));
        Assert.True(party.HasActiveRoomCode);
    }

    [Fact]
    public void Completing_party_releases_its_room_code()
    {
        var party = Party.Create("host-1", RoomCode.Parse("K7XM"), DateTimeOffset.UtcNow);

        party.Complete(DateTimeOffset.UtcNow.AddMinutes(1));

        Assert.Equal(PartyStatus.Completed, party.Status);
        Assert.False(party.HasActiveRoomCode);
        Assert.NotNull(party.CompletedAt);
    }
}
