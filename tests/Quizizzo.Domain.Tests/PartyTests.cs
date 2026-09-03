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

    [Fact]
    public void Active_game_moves_party_to_playing_then_back_to_lobby()
    {
        var party = Party.Create("host-1", RoomCode.Parse("K7XM"), DateTimeOffset.UtcNow);
        var gameInstanceId = Guid.NewGuid();

        party.StartGame(gameInstanceId, "estimate", DateTimeOffset.UtcNow);

        Assert.Equal(PartyStatus.Playing, party.Status);
        Assert.Equal(gameInstanceId, party.CurrentGameInstanceId);
        Assert.Equal("estimate", party.CurrentGameKey);

        party.ReturnToLobby(gameInstanceId);

        Assert.Equal(PartyStatus.Lobby, party.Status);
        Assert.Null(party.CurrentGameInstanceId);
        Assert.Null(party.CurrentGameKey);
    }

    [Fact]
    public void A_second_game_cannot_start_while_one_is_active()
    {
        var party = Party.Create("host-1", RoomCode.Parse("K7XM"), DateTimeOffset.UtcNow);
        party.StartGame(Guid.NewGuid(), "estimate", DateTimeOffset.UtcNow);

        Assert.Throws<InvalidOperationException>(() =>
            party.StartGame(Guid.NewGuid(), "estimate", DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Game_queue_preserves_order_and_is_consumed_one_item_at_a_time()
    {
        var party = Party.Create("host-1", RoomCode.Parse("K7XM"), DateTimeOffset.UtcNow);
        var first = new PartyGameQueueItem(Guid.NewGuid(), "animates", "{\"drawingSecondsPerFrame\":45}");
        var second = new PartyGameQueueItem(Guid.NewGuid(), "estimate", "{}");

        party.ReplaceGameQueue([first, second]);
        var taken = party.TakeNextQueuedGame();

        Assert.Equal(first, taken);
        Assert.Equal([second], party.GameQueue);
    }

    [Fact]
    public void Game_queue_is_bounded_and_cannot_change_during_play()
    {
        var party = Party.Create("host-1", RoomCode.Parse("K7XM"), DateTimeOffset.UtcNow);
        var tooMany = Enumerable.Range(0, Party.MaximumQueuedGames + 1)
            .Select(_ => new PartyGameQueueItem(Guid.NewGuid(), "estimate", "{}"));

        Assert.Throws<InvalidOperationException>(() => party.ReplaceGameQueue(tooMany));

        party.StartGame(Guid.NewGuid(), "estimate", DateTimeOffset.UtcNow);
        Assert.Throws<InvalidOperationException>(() => party.ReplaceGameQueue([]));
    }
}
