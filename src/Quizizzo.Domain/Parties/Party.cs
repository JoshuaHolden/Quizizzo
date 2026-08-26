namespace Quizizzo.Domain.Parties;

public sealed class Party
{
    private Party()
    {
    }

    private Party(PartyId id, string hostUserId, RoomCode roomCode, DateTimeOffset createdAt)
    {
        Id = id;
        HostUserId = hostUserId;
        RoomCode = roomCode;
        Status = PartyStatus.Lobby;
        CreatedAt = createdAt;
    }

    public PartyId Id { get; private set; }
    public string HostUserId { get; private set; } = string.Empty;
    public RoomCode RoomCode { get; private set; }
    public PartyStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? StartedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public Guid? CurrentGameInstanceId { get; private set; }
    public string? CurrentGameKey { get; private set; }

    public bool HasActiveRoomCode => Status is PartyStatus.Created or PartyStatus.Lobby or PartyStatus.Playing or PartyStatus.Paused;

    public static Party Create(string hostUserId, RoomCode roomCode, DateTimeOffset createdAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hostUserId);
        return new Party(PartyId.New(), hostUserId, roomCode, createdAt);
    }

    public bool IsOwnedBy(string hostUserId) =>
        string.Equals(HostUserId, hostUserId, StringComparison.Ordinal);

    public void StartGame(Guid gameInstanceId, string gameKey, DateTimeOffset startedAt)
    {
        if (Status != PartyStatus.Lobby || CurrentGameInstanceId.HasValue)
        {
            throw new InvalidOperationException("A game can start only from the party lobby.");
        }
        if (gameInstanceId == Guid.Empty)
        {
            throw new ArgumentException("A game instance ID is required.", nameof(gameInstanceId));
        }
        ArgumentException.ThrowIfNullOrWhiteSpace(gameKey);

        CurrentGameInstanceId = gameInstanceId;
        CurrentGameKey = gameKey.Trim();
        Status = PartyStatus.Playing;
        StartedAt ??= startedAt;
    }

    public void ReturnToLobby(Guid gameInstanceId)
    {
        if (Status != PartyStatus.Playing || CurrentGameInstanceId != gameInstanceId)
        {
            throw new InvalidOperationException("Only the active game can return this party to its lobby.");
        }

        CurrentGameInstanceId = null;
        CurrentGameKey = null;
        Status = PartyStatus.Lobby;
    }

    public void Complete(DateTimeOffset completedAt)
    {
        if (Status is PartyStatus.Completed or PartyStatus.Abandoned)
        {
            return;
        }

        Status = PartyStatus.Completed;
        CurrentGameInstanceId = null;
        CurrentGameKey = null;
        CompletedAt = completedAt;
    }

    public void Abandon(DateTimeOffset abandonedAt)
    {
        if (Status is PartyStatus.Completed or PartyStatus.Abandoned)
        {
            return;
        }

        Status = PartyStatus.Abandoned;
        CurrentGameInstanceId = null;
        CurrentGameKey = null;
        CompletedAt = abandonedAt;
    }
}
