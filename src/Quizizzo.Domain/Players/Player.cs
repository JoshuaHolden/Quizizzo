using Quizizzo.Domain.Parties;

namespace Quizizzo.Domain.Players;

public sealed class Player
{
    private Player()
    {
    }

    private Player(
        PlayerId id,
        PartyId partyId,
        PlayerName displayName,
        CharacterDefinition character,
        string sessionTokenHash,
        DateTimeOffset joinedAt)
    {
        Id = id;
        PartyId = partyId;
        DisplayName = displayName;
        Character = character;
        SessionTokenHash = sessionTokenHash;
        Status = PlayerStatus.Connected;
        JoinedAt = joinedAt;
        LastSeenAt = joinedAt;
    }

    public PlayerId Id { get; private set; }
    public PartyId PartyId { get; private set; }
    public PlayerName DisplayName { get; private set; }
    public CharacterDefinition Character { get; private set; } = null!;
    public int Score { get; private set; }
    public PlayerStatus Status { get; private set; }
    public DateTimeOffset JoinedAt { get; private set; }
    public DateTimeOffset LastSeenAt { get; private set; }
    public string SessionTokenHash { get; private set; } = string.Empty;

    public bool IsPartyMember => Status is PlayerStatus.Connected or PlayerStatus.Disconnected;

    public static Player Create(
        PartyId partyId,
        PlayerName displayName,
        CharacterDefinition character,
        string sessionTokenHash,
        DateTimeOffset joinedAt)
    {
        ArgumentNullException.ThrowIfNull(character);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionTokenHash);
        return new Player(PlayerId.New(), partyId, displayName, character, sessionTokenHash, joinedAt);
    }

    public void Reconnect(DateTimeOffset seenAt)
    {
        if (Status is PlayerStatus.Left or PlayerStatus.Kicked)
        {
            throw new InvalidOperationException("This player can no longer rejoin the party.");
        }

        Status = PlayerStatus.Connected;
        LastSeenAt = seenAt;
    }

    public void MarkDisconnected(DateTimeOffset seenAt)
    {
        if (Status == PlayerStatus.Connected)
        {
            Status = PlayerStatus.Disconnected;
            LastSeenAt = seenAt;
        }
    }

    public void Kick(DateTimeOffset seenAt)
    {
        if (Status is PlayerStatus.Left or PlayerStatus.Kicked)
        {
            return;
        }

        Status = PlayerStatus.Kicked;
        LastSeenAt = seenAt;
    }

    public void SetScore(int score)
    {
        if (score < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(score), "Player scores cannot be negative.");
        }
        Score = score;
    }
}
