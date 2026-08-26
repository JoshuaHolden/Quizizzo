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

    public bool HasActiveRoomCode => Status is PartyStatus.Created or PartyStatus.Lobby or PartyStatus.Playing or PartyStatus.Paused;

    public static Party Create(string hostUserId, RoomCode roomCode, DateTimeOffset createdAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hostUserId);
        return new Party(PartyId.New(), hostUserId, roomCode, createdAt);
    }

    public bool IsOwnedBy(string hostUserId) =>
        string.Equals(HostUserId, hostUserId, StringComparison.Ordinal);

    public void Complete(DateTimeOffset completedAt)
    {
        if (Status is PartyStatus.Completed or PartyStatus.Abandoned)
        {
            return;
        }

        Status = PartyStatus.Completed;
        CompletedAt = completedAt;
    }

    public void Abandon(DateTimeOffset abandonedAt)
    {
        if (Status is PartyStatus.Completed or PartyStatus.Abandoned)
        {
            return;
        }

        Status = PartyStatus.Abandoned;
        CompletedAt = abandonedAt;
    }
}
