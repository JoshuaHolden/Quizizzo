namespace Quizizzo.Web.Realtime;

public interface IPartyRealtimeNotifier
{
    Task PartyChangedAsync(Guid partyId, string reason, CancellationToken cancellationToken = default);
    Task DisplaySessionChangedAsync(Guid displaySessionId, string reason, CancellationToken cancellationToken = default);
}
