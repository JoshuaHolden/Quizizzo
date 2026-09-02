using Quizizzo.Application.Abstractions;
using Quizizzo.Application.Games;
using Quizizzo.Application.Parties;
using Quizizzo.Domain.Displays;
using Quizizzo.Domain.Parties;
using Quizizzo.Domain.Players;
using Quizizzo.GameContracts;

namespace Quizizzo.Application.Tests;

public sealed class PartyGameServiceTests
{
    [Fact]
    public async Task Start_records_the_active_game_and_preserves_existing_party_scores()
    {
        var fixture = new Fixture();
        fixture.First.SetScore(250);

        var started = await fixture.Service.StartAsync(
            fixture.Party.Id.Value, Fixture.HostId, "estimate");

        Assert.Equal(PartyStatus.Playing, fixture.Party.Status);
        Assert.Equal(started.GameInstanceId, fixture.Party.CurrentGameInstanceId);
        Assert.Equal("estimate", fixture.Party.CurrentGameKey);
        var request = Assert.Single(fixture.Runtime.Starts);
        Assert.Equal(250, request.Participants.Single(player =>
            player.PlayerId == fixture.First.Id.Value).StartingScore);
    }

    [Fact]
    public async Task Start_forwards_game_configuration_to_the_runtime()
    {
        var fixture = new Fixture();
        var configuration = GameJson.From(new { DrawingSecondsPerFrame = 45 });

        await fixture.Service.StartAsync(
            fixture.Party.Id.Value, Fixture.HostId, "animates", configuration);

        Assert.Equal(45, Assert.Single(fixture.Runtime.Starts).Configuration
            .GetProperty("DrawingSecondsPerFrame").GetInt32());
    }

    [Fact]
    public async Task Completed_game_persists_scores_returns_to_lobby_and_allows_a_second_game()
    {
        var fixture = new Fixture();
        var firstGame = await fixture.Service.StartAsync(
            fixture.Party.Id.Value, Fixture.HostId, "estimate");
        fixture.Runtime.NextResult = new RuntimeGameCommandResult(
            true,
            false,
            "Completed",
            null,
            true,
            null,
            null,
            new Dictionary<Guid, int>
            {
                [fixture.First.Id.Value] = 2600,
                [fixture.Second.Id.Value] = 2200
            });

        var completed = await fixture.Service.ExecuteHostActionAsync(
            fixture.Party.Id.Value,
            Fixture.HostId,
            Guid.NewGuid(),
            "estimate.advance",
            GameJson.Empty);
        var secondGame = await fixture.Service.StartAsync(
            fixture.Party.Id.Value, Fixture.HostId, "estimate");

        Assert.True(completed.IsComplete);
        Assert.Equal(2600, fixture.First.Score);
        Assert.Equal(2200, fixture.Second.Score);
        Assert.Equal(1, fixture.First.TotalWins);
        Assert.Equal(1, fixture.First.GameWinCounts()["estimate"]);
        Assert.Equal(0, fixture.Second.TotalWins);
        Assert.NotEqual(firstGame.GameInstanceId, secondGame.GameInstanceId);
        Assert.Equal(2, fixture.Runtime.Starts.Count);
        Assert.Equal(PartyStatus.Playing, fixture.Party.Status);
    }

    [Fact]
    public async Task Completed_games_record_each_title_win_and_count_positive_ties()
    {
        var fixture = new Fixture();

        await fixture.CompleteGameAsync("estimate", 500, 300);
        await fixture.CompleteGameAsync("animates", 650, 600);
        await fixture.CompleteGameAsync("estimate", 750, 700);

        Assert.Equal(2, fixture.First.TotalWins);
        Assert.Equal(2, fixture.First.GameWinCounts()["estimate"]);
        Assert.Equal(2, fixture.Second.TotalWins);
        Assert.Equal(1, fixture.Second.GameWinCounts()["animates"]);
        Assert.Equal(1, fixture.Second.GameWinCounts()["estimate"]);
    }

    [Fact]
    public async Task A_different_host_cannot_start_or_view_the_party_game()
    {
        var fixture = new Fixture();

        await Assert.ThrowsAsync<PartyAccessDeniedException>(() =>
            fixture.Service.StartAsync(fixture.Party.Id.Value, "other-host", "estimate"));
        await Assert.ThrowsAsync<PartyAccessDeniedException>(() =>
            fixture.Service.GetHostViewAsync(fixture.Party.Id.Value, "other-host"));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            fixture.Service.GetDisplayViewAsync(
                fixture.Party.Id.Value, Guid.NewGuid().ToString("N")));
    }

    [Fact]
    public async Task Completed_snapshot_is_finalized_during_recovery_after_a_process_interruption()
    {
        var fixture = new Fixture();
        var started = await fixture.Service.StartAsync(
            fixture.Party.Id.Value, Fixture.HostId, "estimate");
        fixture.Runtime.NextView = new RuntimeGameView(
            new GameInstanceId(started.GameInstanceId),
            "estimate",
            GameAudienceRole.Host,
            "Completed",
            12,
            null,
            true,
            GameJson.Empty,
            new Dictionary<Guid, int>
            {
                [fixture.First.Id.Value] = 1700,
                [fixture.Second.Id.Value] = 900
            });

        var recovered = await fixture.Service.GetHostViewAsync(
            fixture.Party.Id.Value, Fixture.HostId);

        Assert.Null(recovered);
        Assert.Equal(PartyStatus.Lobby, fixture.Party.Status);
        Assert.Equal(1700, fixture.First.Score);
        Assert.Equal(900, fixture.Second.Score);
    }

    private sealed class Fixture
    {
        public const string HostId = "host-user";
        private static readonly DateTimeOffset Now =
            new(2026, 8, 27, 12, 0, 0, TimeSpan.Zero);

        public Fixture()
        {
            Party = Party.Create(HostId, RoomCode.Parse("K7XM"), Now);
            First = CreatePlayer("First", "TOKEN-1");
            Second = CreatePlayer("Second", "TOKEN-2");
            PartyRepository.Parties.Add(Party);
            PlayerRepository.Players.AddRange([First, Second]);
            Service = new PartyGameService(
                PartyRepository,
                PlayerRepository,
                DisplayRepository,
                Runtime,
                new PartyMutationCoordinator(),
                new FixedTimeProvider());
        }

        public Party Party { get; }
        public Player First { get; }
        public Player Second { get; }
        public FakePartyRepository PartyRepository { get; } = new();
        public FakePlayerRepository PlayerRepository { get; } = new();
        public FakeDisplayRepository DisplayRepository { get; } = new();
        public FakeRuntime Runtime { get; } = new();
        public PartyGameService Service { get; }

        private Player CreatePlayer(string name, string token) => Player.Create(
            Party.Id,
            PlayerName.Parse(name),
            new CharacterDefinition(
                CharacterBodyType.Round,
                "#4361EE",
                CharacterEyes.Bright,
                CharacterMouth.Smile,
                CharacterAccessory.None),
            token,
            Now);

        public async Task CompleteGameAsync(string gameKey, int firstScore, int secondScore)
        {
            await Service.StartAsync(Party.Id.Value, HostId, gameKey);
            Runtime.NextResult = new RuntimeGameCommandResult(
                true, false, "Completed", null, true, null, null,
                new Dictionary<Guid, int>
                {
                    [First.Id.Value] = firstScore,
                    [Second.Id.Value] = secondScore
                });
            await Service.ExecuteHostActionAsync(
                Party.Id.Value, HostId, Guid.NewGuid(), "complete", GameJson.Empty);
        }
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() =>
            new(2026, 8, 27, 12, 0, 0, TimeSpan.Zero);
    }

    private sealed class FakeRuntime : IPartyGameRuntime
    {
        public List<RuntimeGameStart> Starts { get; } = [];
        public RuntimeGameCommandResult NextResult { get; set; } = new(
            true, false, "Results", null, false, null, null, new Dictionary<Guid, int>());
        public RuntimeGameView? NextView { get; set; }

        public IReadOnlyList<GameDescriptor> ListGames() =>
            [new GameDescriptor("estimate", "Estimate", 2, 12)];

        public Task<RuntimeGameStatus> StartAsync(
            RuntimeGameStart request,
            CancellationToken cancellationToken = default)
        {
            Starts.Add(request);
            return Task.FromResult(new RuntimeGameStatus(
                request.GameInstanceId, "Answering", DateTimeOffset.UtcNow.AddSeconds(30), false));
        }

        public Task<RuntimeGameCommandResult> ExecuteAsync(
            RuntimeGameCommand command,
            CancellationToken cancellationToken = default) => Task.FromResult(NextResult);

        public Task<RuntimeGameView> GetViewAsync(
            GameInstanceId gameInstanceId,
            GameAudienceRole role,
            string subjectId,
            CancellationToken cancellationToken = default) => Task.FromResult(
                NextView ?? throw new NotSupportedException("No fake runtime view was configured."));
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
        public Task<Party?> GetByRoomCodeAsync(RoomCode roomCode, CancellationToken cancellationToken) =>
            Task.FromResult(Parties.SingleOrDefault(party => party.RoomCode == roomCode));
        public Task<Party?> GetActiveByHostAsync(string hostUserId, CancellationToken cancellationToken) =>
            Task.FromResult(Parties.SingleOrDefault(party => party.HostUserId == hostUserId));
        public Task<IReadOnlyList<Party>> ListRecentByHostAsync(
            string hostUserId, int limit, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Party>>(Parties.Where(party => party.HostUserId == hostUserId).Take(limit).ToArray());
        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FakePlayerRepository : IPlayerRepository
    {
        public List<Player> Players { get; } = [];
        public Task<int> CountMembersAsync(PartyId partyId, CancellationToken cancellationToken) =>
            Task.FromResult(Players.Count(player => player.PartyId == partyId && player.IsPartyMember));
        public Task AddAsync(Player player, CancellationToken cancellationToken)
        {
            Players.Add(player);
            return Task.CompletedTask;
        }
        public Task<Player?> GetByIdAsync(PlayerId playerId, CancellationToken cancellationToken) =>
            Task.FromResult(Players.SingleOrDefault(player => player.Id == playerId));
        public Task<Player?> GetBySessionTokenHashAsync(string tokenHash, CancellationToken cancellationToken) =>
            Task.FromResult(Players.SingleOrDefault(player => player.SessionTokenHash == tokenHash));
        public Task<IReadOnlyList<Player>> ListMembersAsync(PartyId partyId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Player>>(Players.Where(player => player.PartyId == partyId && player.IsPartyMember).ToArray());
        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FakeDisplayRepository : IDisplaySessionRepository
    {
        public List<DisplaySession> Displays { get; } = [];
        public Task<bool> PairingCodeExistsAsync(string pairingCode, CancellationToken cancellationToken) =>
            Task.FromResult(Displays.Any(display => display.PairingCode == pairingCode));
        public Task AddAsync(DisplaySession displaySession, CancellationToken cancellationToken)
        {
            Displays.Add(displaySession);
            return Task.CompletedTask;
        }
        public Task<DisplaySession?> GetByIdAsync(DisplaySessionId displaySessionId, CancellationToken cancellationToken) =>
            Task.FromResult(Displays.SingleOrDefault(display => display.Id == displaySessionId));
        public Task<DisplaySession?> GetBySessionTokenHashAsync(string tokenHash, CancellationToken cancellationToken) =>
            Task.FromResult(Displays.SingleOrDefault(display => display.SessionTokenHash == tokenHash));
        public Task<DisplaySession?> GetByPairingCodeAsync(string pairingCode, CancellationToken cancellationToken) =>
            Task.FromResult(Displays.SingleOrDefault(display => display.PairingCode == pairingCode));
        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
