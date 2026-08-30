namespace Quizizzo.Web.Realtime;

public sealed record PartyStateChangedMessage(Guid? PartyId, string Reason, DateTimeOffset OccurredAtUtc);
public sealed record PlayerReactionMessage(Guid PlayerId, string Reaction, DateTimeOffset OccurredAtUtc);

public sealed record PartyPresenceSnapshot(int Hosts, int Players, int Displays)
{
    public bool HostConnected => Hosts > 0;
    public bool DisplayConnected => Displays > 0;
}

public interface IPartyClient
{
    Task StateChanged(PartyStateChangedMessage message);
    Task PlayerReacted(PlayerReactionMessage message);
}
