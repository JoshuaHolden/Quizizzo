using Quizizzo.Web.Realtime;

namespace Quizizzo.IntegrationTests;

public sealed class PlayerReactionLimiterTests
{
    [Fact]
    public void Reactions_are_limited_per_durable_player_and_reopen_after_two_seconds()
    {
        var time = new ManualTimeProvider(new DateTimeOffset(2026, 8, 30, 12, 0, 0, TimeSpan.Zero));
        var limiter = new PlayerReactionLimiter(time);
        var firstPlayer = Guid.NewGuid();

        Assert.True(limiter.TryAcquire(firstPlayer));
        Assert.False(limiter.TryAcquire(firstPlayer));
        Assert.True(limiter.TryAcquire(Guid.NewGuid()));

        time.Advance(TimeSpan.FromSeconds(2));
        Assert.True(limiter.TryAcquire(firstPlayer));
    }

    private sealed class ManualTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(TimeSpan duration) => now += duration;
    }
}
