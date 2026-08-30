using System.Collections.Concurrent;

namespace Quizizzo.Web.Realtime;

public sealed class PlayerReactionLimiter(TimeProvider timeProvider)
{
    private static readonly TimeSpan MinimumInterval = TimeSpan.FromSeconds(2);
    private readonly ConcurrentDictionary<Guid, DateTimeOffset> lastReactions = new();

    public bool TryAcquire(Guid playerId)
    {
        var now = timeProvider.GetUtcNow();
        while (true)
        {
            if (!lastReactions.TryGetValue(playerId, out var previous))
            {
                return lastReactions.TryAdd(playerId, now) || TryAcquire(playerId);
            }
            if (now - previous < MinimumInterval)
            {
                return false;
            }
            if (lastReactions.TryUpdate(playerId, now, previous))
            {
                return true;
            }
        }
    }
}
