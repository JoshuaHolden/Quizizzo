namespace Quizizzo.Domain.Players;

public sealed class PlayerGameWin
{
    private PlayerGameWin()
    {
    }

    internal PlayerGameWin(Guid gameInstanceId, string gameKey, DateTimeOffset wonAt)
    {
        if (gameInstanceId == Guid.Empty)
        {
            throw new ArgumentException("A game instance ID is required.", nameof(gameInstanceId));
        }
        ArgumentException.ThrowIfNullOrWhiteSpace(gameKey);

        GameInstanceId = gameInstanceId;
        GameKey = gameKey.Trim().ToLowerInvariant();
        WonAt = wonAt;
    }

    public Guid GameInstanceId { get; private set; }
    public string GameKey { get; private set; } = string.Empty;
    public DateTimeOffset WonAt { get; private set; }
}
