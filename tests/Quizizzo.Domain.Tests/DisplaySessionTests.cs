using Quizizzo.Domain.Displays;
using Quizizzo.Domain.Parties;

namespace Quizizzo.Domain.Tests;

public sealed class DisplaySessionTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 26, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Party_owner_can_pair_an_unexpired_display()
    {
        var party = Party.Create("host-1", RoomCode.Parse("K7XM"), Now);
        var display = DisplaySession.Create("HASH", "ABCDEFGH", Now, TimeSpan.FromMinutes(15));

        display.Pair(party, "host-1", Now.AddMinutes(1));

        Assert.True(display.IsPaired);
        Assert.Equal(party.Id, display.PartyId);
    }

    [Fact]
    public void Another_host_cannot_pair_the_display()
    {
        var party = Party.Create("host-1", RoomCode.Parse("K7XM"), Now);
        var display = DisplaySession.Create("HASH", "ABCDEFGH", Now, TimeSpan.FromMinutes(15));

        var exception = Assert.Throws<InvalidOperationException>(
            () => display.Pair(party, "host-2", Now.AddMinutes(1)));

        Assert.Contains("owner", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(display.IsPaired);
    }

    [Fact]
    public void Expired_pairing_code_is_rejected()
    {
        var party = Party.Create("host-1", RoomCode.Parse("K7XM"), Now);
        var display = DisplaySession.Create("HASH", "ABCDEFGH", Now, TimeSpan.FromMinutes(15));

        Assert.Throws<InvalidOperationException>(
            () => display.Pair(party, "host-1", Now.AddMinutes(16)));
    }

    [Fact]
    public void Pairing_lifetimes_must_be_positive_when_created_or_renewed()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            DisplaySession.Create("HASH", "ABCDEFGH", Now, TimeSpan.Zero));
        var display = DisplaySession.Create(
            "HASH", "ABCDEFGH", Now, TimeSpan.FromMinutes(15));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            display.RenewPairingCode("HGFEDCBA", Now, TimeSpan.Zero));
    }
}
