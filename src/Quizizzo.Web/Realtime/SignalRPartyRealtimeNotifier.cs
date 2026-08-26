using Microsoft.AspNetCore.SignalR;

namespace Quizizzo.Web.Realtime;

public sealed class SignalRPartyRealtimeNotifier(
    IHubContext<PartyHub, IPartyClient> hubContext,
    TimeProvider timeProvider) : IPartyRealtimeNotifier
{
    public Task PartyChangedAsync(Guid partyId, string reason, CancellationToken cancellationToken = default) =>
        hubContext.Clients.Group(RealtimeGroups.Party(partyId)).StateChanged(
            new PartyStateChangedMessage(partyId, reason, timeProvider.GetUtcNow()));

    public Task DisplaySessionChangedAsync(
        Guid displaySessionId,
        string reason,
        CancellationToken cancellationToken = default) =>
        hubContext.Clients.Group(RealtimeGroups.DisplaySession(displaySessionId)).StateChanged(
            new PartyStateChangedMessage(null, reason, timeProvider.GetUtcNow()));
}
