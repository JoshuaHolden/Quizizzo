using Quizizzo.Domain.Parties;

namespace Quizizzo.Domain.Displays;

public sealed class DisplaySession
{
    private DisplaySession()
    {
    }

    private DisplaySession(
        DisplaySessionId id,
        string sessionTokenHash,
        string pairingCode,
        DateTimeOffset createdAt,
        DateTimeOffset pairingExpiresAt)
    {
        Id = id;
        SessionTokenHash = sessionTokenHash;
        PairingCode = pairingCode;
        CreatedAt = createdAt;
        LastSeenAt = createdAt;
        PairingExpiresAt = pairingExpiresAt;
    }

    public DisplaySessionId Id { get; private set; }
    public string SessionTokenHash { get; private set; } = string.Empty;
    public string PairingCode { get; private set; } = string.Empty;
    public PartyId? PartyId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset LastSeenAt { get; private set; }
    public DateTimeOffset PairingExpiresAt { get; private set; }
    public DateTimeOffset? PairedAt { get; private set; }

    public bool IsPaired => PartyId.HasValue;

    public static DisplaySession Create(
        string sessionTokenHash,
        string pairingCode,
        DateTimeOffset createdAt,
        TimeSpan pairingLifetime)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionTokenHash);
        ArgumentException.ThrowIfNullOrWhiteSpace(pairingCode);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(pairingLifetime, TimeSpan.Zero);

        return new DisplaySession(
            DisplaySessionId.New(),
            sessionTokenHash,
            pairingCode,
            createdAt,
            createdAt.Add(pairingLifetime));
    }

    public void MarkSeen(DateTimeOffset seenAt) => LastSeenAt = seenAt;

    public void RenewPairingCode(string pairingCode, DateTimeOffset renewedAt, TimeSpan pairingLifetime)
    {
        if (IsPaired)
        {
            throw new InvalidOperationException("A paired display does not need a new pairing code.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(pairingCode);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(pairingLifetime, TimeSpan.Zero);
        PairingCode = pairingCode;
        PairingExpiresAt = renewedAt.Add(pairingLifetime);
        LastSeenAt = renewedAt;
    }

    public void Pair(Party party, string hostUserId, DateTimeOffset pairedAt)
    {
        ArgumentNullException.ThrowIfNull(party);
        if (!party.IsOwnedBy(hostUserId))
        {
            throw new InvalidOperationException("Only the party owner can pair a display.");
        }

        if (pairedAt > PairingExpiresAt)
        {
            throw new InvalidOperationException("The display pairing code has expired.");
        }

        if (PartyId.HasValue && PartyId != party.Id)
        {
            throw new InvalidOperationException("The display is already paired to another party.");
        }

        PartyId = party.Id;
        PairedAt ??= pairedAt;
        LastSeenAt = pairedAt;
    }
}
