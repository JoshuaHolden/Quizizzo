namespace Quizizzo.Application.Displays;

public sealed record DisplaySessionView(
    Guid DisplaySessionId,
    string PairingCode,
    DateTimeOffset PairingExpiresAt,
    bool IsPaired,
    Guid? PartyId,
    string? RoomCode);

public sealed record RestoredDisplaySession(string SessionToken, bool IsNew, DisplaySessionView View);
