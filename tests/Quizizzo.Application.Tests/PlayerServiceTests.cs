using Quizizzo.Application.Abstractions;
using Quizizzo.Application.Parties;
using Quizizzo.Application.Players;
using Quizizzo.Domain;
using Quizizzo.Domain.Parties;
using Quizizzo.Domain.Players;

namespace Quizizzo.Application.Tests;

public sealed class PlayerServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 26, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Join_creates_a_durable_anonymous_identity_with_a_hashed_token()
    {
        var fixture = new Fixture();

        var joined = await fixture.Service.JoinAsync("k7xm", "  Joshua  ");

        var stored = Assert.Single(fixture.PlayerRepository.Players);
        Assert.True(joined.IsNew);
        Assert.Equal("raw-token", joined.SessionToken);
        Assert.Equal("HASHED:raw-token", stored.SessionTokenHash);
        Assert.Equal("Joshua", stored.DisplayName.Value);
        Assert.Equal(fixture.Party.Id.Value, joined.View.PartyId);
        Assert.Equal("#4361EE", joined.View.Character.PrimaryColour);
    }

    [Fact]
    public async Task Join_persists_the_players_validated_avatar_selection()
    {
        var fixture = new Fixture();
        var selection = new CharacterSelection(
            CharacterPresentation.Woman,
            CharacterSkinTone.Tint7,
            CharacterHairColour.Red,
            CharacterShirtColour.Green,
            CharacterTrouserColour.Tan,
            CharacterTrouserLength.Shorts,
            CharacterShoeColour.Black);

        var joined = await fixture.Service.JoinAsync("K7XM", "Designer", null, selection);

        Assert.Equal(CharacterPresentation.Woman, joined.View.Character.Presentation);
        Assert.Equal(CharacterSkinTone.Tint7, joined.View.Character.SkinTone);
        Assert.Equal(CharacterHairColour.Red, joined.View.Character.HairColour);
        Assert.Equal(CharacterShirtColour.Green, joined.View.Character.ShirtColour);
        Assert.Equal(CharacterTrouserColour.Tan, joined.View.Character.TrouserColour);
        Assert.Equal(CharacterTrouserLength.Shorts, joined.View.Character.TrouserLength);
        Assert.Equal(CharacterShoeColour.Black, joined.View.Character.ShoeColour);
    }

    [Fact]
    public async Task Joining_again_with_the_same_cookie_restores_identity_without_a_duplicate()
    {
        var fixture = new Fixture();
        var original = await fixture.Service.JoinAsync("K7XM", "Joshua");

        var restored = await fixture.Service.JoinAsync("K7XM", "Ignored new name", original.SessionToken);

        Assert.False(restored.IsNew);
        Assert.Equal(original.View.PlayerId, restored.View.PlayerId);
        Assert.Equal("Joshua", restored.View.DisplayName);
        Assert.Single(fixture.PlayerRepository.Players);
    }

    [Fact]
    public async Task Thirteenth_party_member_is_rejected()
    {
        var fixture = new Fixture();
        for (var index = 0; index < QuizizzoLimits.MaximumPlayers; index++)
        {
            fixture.PlayerRepository.Players.Add(Player.Create(
                fixture.Party.Id,
                PlayerName.Parse($"Player {index}"),
                Fixture.Character,
                $"HASH-{index}",
                Now));
        }

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => fixture.Service.JoinAsync("K7XM", "Too Late"));

        Assert.Contains("full", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Concurrent_joins_cannot_exceed_the_party_limit()
    {
        var fixture = new Fixture();
        for (var index = 0; index < QuizizzoLimits.MaximumPlayers - 1; index++)
        {
            fixture.PlayerRepository.Players.Add(Player.Create(
                fixture.Party.Id,
                PlayerName.Parse($"Player {index}"),
                Fixture.Character,
                $"HASH-{index}",
                Now));
        }

        var attempts = await Task.WhenAll(
            AttemptJoinAsync(fixture.Service, "Final A"),
            AttemptJoinAsync(fixture.Service, "Final B"));

        Assert.Single(attempts, succeeded => succeeded);
        Assert.Equal(
            QuizizzoLimits.MaximumPlayers,
            fixture.PlayerRepository.Players.Count(player => player.IsPartyMember));
    }

    [Fact]
    public async Task Party_owner_can_kick_a_player_from_the_lobby()
    {
        var fixture = new Fixture();
        var joined = await fixture.Service.JoinAsync("K7XM", "Goodbye");

        await fixture.Service.KickAsync(fixture.Party.Id.Value, "host-1", joined.View.PlayerId);

        Assert.Equal(PlayerStatus.Kicked, Assert.Single(fixture.PlayerRepository.Players).Status);
        Assert.Empty(await fixture.Service.ListForHostAsync(fixture.Party.Id.Value, "host-1"));
    }

    [Fact]
    public async Task Kicked_player_cookie_can_create_a_fresh_membership_when_joining_again()
    {
        var fixture = new Fixture();
        var original = await fixture.Service.JoinAsync("K7XM", "First player");
        await fixture.Service.KickAsync(
            fixture.Party.Id.Value,
            "host-1",
            original.View.PlayerId);

        var rejoined = await fixture.Service.JoinAsync(
            "K7XM",
            "Fresh player",
            original.SessionToken);

        Assert.True(rejoined.IsNew);
        Assert.NotEqual(original.View.PlayerId, rejoined.View.PlayerId);
        Assert.NotEqual(original.SessionToken, rejoined.SessionToken);
        Assert.Equal("Fresh player", rejoined.View.DisplayName);
        Assert.Equal(2, fixture.PlayerRepository.Players.Count);
        Assert.Equal(PlayerStatus.Kicked, fixture.PlayerRepository.Players[0].Status);
        Assert.Equal(PlayerStatus.Connected, fixture.PlayerRepository.Players[1].Status);
        Assert.Single(await fixture.Service.ListForHostAsync(
            fixture.Party.Id.Value,
            "host-1"));
    }

    [Fact]
    public async Task Different_host_cannot_kick_a_player()
    {
        var fixture = new Fixture();
        var joined = await fixture.Service.JoinAsync("K7XM", "Staying");

        await Assert.ThrowsAsync<PartyAccessDeniedException>(() =>
            fixture.Service.KickAsync(fixture.Party.Id.Value, "intruder", joined.View.PlayerId));

        Assert.Equal(PlayerStatus.Connected, Assert.Single(fixture.PlayerRepository.Players).Status);
    }

    private static async Task<bool> AttemptJoinAsync(PlayerService service, string name)
    {
        try
        {
            await service.JoinAsync("K7XM", name);
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private sealed class Fixture
    {
        public static readonly CharacterDefinition Character = new(
            CharacterBodyType.Round,
            "#4361EE",
            CharacterEyes.Starry,
            CharacterMouth.Grin,
            CharacterAccessory.PartyHat);

        public Fixture()
        {
            Party = Party.Create("host-1", RoomCode.Parse("K7XM"), Now);
            PartyRepository.Parties.Add(Party);
            Service = new PlayerService(
                PartyRepository,
                PlayerRepository,
                new FakeCredentials(),
                new FakeCharacterGenerator(),
                new PartyMutationCoordinator(),
                new FixedTimeProvider());
        }

        public Party Party { get; }
        public FakePartyRepository PartyRepository { get; } = new();
        public FakePlayerRepository PlayerRepository { get; } = new();
        public PlayerService Service { get; }
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class FakeCredentials : IPlayerCredentialService
    {
        private int generatedTokenCount;

        public string GenerateSessionToken()
        {
            generatedTokenCount++;
            return generatedTokenCount == 1 ? "raw-token" : $"raw-token-{generatedTokenCount}";
        }

        public string HashSessionToken(string sessionToken) => $"HASHED:{sessionToken}";
    }

    private sealed class FakeCharacterGenerator : ICharacterGenerator
    {
        public CharacterDefinition Generate() => Fixture.Character;
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
        public Task<Player?> GetBySessionTokenHashAsync(string tokenHash, CancellationToken cancellationToken) =>
            Task.FromResult(Players.SingleOrDefault(player => player.SessionTokenHash == tokenHash));
        public Task<Player?> GetByIdAsync(PlayerId playerId, CancellationToken cancellationToken) =>
            Task.FromResult(Players.SingleOrDefault(player => player.Id == playerId));
        public Task<IReadOnlyList<Player>> ListMembersAsync(PartyId partyId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Player>>(Players.Where(player => player.PartyId == partyId && player.IsPartyMember).ToArray());
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
        public Task<Party?> GetByRoomCodeAsync(RoomCode roomCode, CancellationToken cancellationToken) =>
            Task.FromResult(Parties.SingleOrDefault(party => party.RoomCode == roomCode && party.HasActiveRoomCode));
        public Task<Party?> GetActiveByHostAsync(string hostUserId, CancellationToken cancellationToken) =>
            Task.FromResult(Parties.SingleOrDefault(party => party.HostUserId == hostUserId && party.HasActiveRoomCode));
        public Task<IReadOnlyList<Party>> ListRecentByHostAsync(string hostUserId, int limit, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Party>>([]);
        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
