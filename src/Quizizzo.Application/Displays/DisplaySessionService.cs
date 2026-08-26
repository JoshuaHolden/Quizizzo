using Quizizzo.Application.Abstractions;
using Quizizzo.Application.Parties;
using Quizizzo.Domain.Displays;

namespace Quizizzo.Application.Displays;

public sealed class DisplaySessionService(
    IDisplaySessionRepository displaySessions,
    IPartyRepository parties,
    IDisplayCredentialService credentials,
    TimeProvider timeProvider)
{
    private static readonly TimeSpan PairingLifetime = TimeSpan.FromMinutes(15);
    private const int PairingCodeAttempts = 32;

    public async Task<RestoredDisplaySession> RestoreOrCreateAsync(
        string? sessionToken,
        CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow();
        if (!string.IsNullOrWhiteSpace(sessionToken))
        {
            var existing = await displaySessions.GetBySessionTokenHashAsync(
                credentials.HashSessionToken(sessionToken), cancellationToken);
            if (existing is not null)
            {
                existing.MarkSeen(now);
                if (!existing.IsPaired && existing.PairingExpiresAt <= now)
                {
                    existing.RenewPairingCode(
                        await AllocatePairingCodeAsync(cancellationToken), now, PairingLifetime);
                }

                await displaySessions.SaveChangesAsync(cancellationToken);
                return new RestoredDisplaySession(sessionToken, false, await MapAsync(existing, cancellationToken));
            }
        }

        var newToken = credentials.GenerateSessionToken();
        var displaySession = DisplaySession.Create(
            credentials.HashSessionToken(newToken),
            await AllocatePairingCodeAsync(cancellationToken),
            now,
            PairingLifetime);
        await displaySessions.AddAsync(displaySession, cancellationToken);
        await displaySessions.SaveChangesAsync(cancellationToken);
        return new RestoredDisplaySession(newToken, true, await MapAsync(displaySession, cancellationToken));
    }

    public async Task<DisplaySessionView> PairAsync(
        string pairingCode,
        Guid partyId,
        string hostUserId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pairingCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(hostUserId);

        var displaySession = await displaySessions.GetByPairingCodeAsync(
            pairingCode.Trim().ToUpperInvariant(), cancellationToken)
            ?? throw new InvalidOperationException("The display pairing code is invalid.");
        var party = await parties.GetByIdAsync(new(partyId), cancellationToken)
            ?? throw new PartyNotFoundException();
        if (!party.IsOwnedBy(hostUserId))
        {
            throw new PartyAccessDeniedException();
        }

        displaySession.Pair(party, hostUserId, timeProvider.GetUtcNow());
        await displaySessions.SaveChangesAsync(cancellationToken);
        return await MapAsync(displaySession, cancellationToken);
    }

    private async Task<string> AllocatePairingCodeAsync(CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < PairingCodeAttempts; attempt++)
        {
            var pairingCode = credentials.GeneratePairingCode();
            if (!await displaySessions.PairingCodeExistsAsync(pairingCode, cancellationToken))
            {
                return pairingCode;
            }
        }

        throw new InvalidOperationException("A unique display pairing code could not be allocated.");
    }

    private async Task<DisplaySessionView> MapAsync(
        DisplaySession session,
        CancellationToken cancellationToken)
    {
        string? roomCode = null;
        if (session.PartyId is { } partyId)
        {
            roomCode = (await parties.GetByIdAsync(partyId, cancellationToken))?.RoomCode.Value;
        }

        return new DisplaySessionView(
            session.Id.Value,
            session.PairingCode,
            session.PairingExpiresAt,
            session.IsPaired,
            session.PartyId?.Value,
            roomCode);
    }
}
