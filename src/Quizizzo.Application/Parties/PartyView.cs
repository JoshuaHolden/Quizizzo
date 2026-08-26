using Quizizzo.Domain.Parties;

namespace Quizizzo.Application.Parties;

public sealed record PartyView(
    Guid PartyId,
    string RoomCode,
    PartyStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt);
