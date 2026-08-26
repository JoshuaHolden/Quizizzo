using System.Text.Json;

namespace Quizizzo.GameContracts;

public enum GameAudienceRole
{
    Host,
    Display,
    Player
}

public sealed record GameViewContext(
    GameAudienceRole Role,
    string SubjectId,
    Guid? PlayerId);

public sealed record GameViewPayload(JsonElement Data);
