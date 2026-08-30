using Microsoft.Extensions.Diagnostics.HealthChecks;
using StackExchange.Redis;

namespace Quizizzo.Web.Realtime;

public sealed class RedisBackplaneHealthCheck(string connectionString) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var configuration = ConfigurationOptions.Parse(connectionString);
            configuration.AbortOnConnectFail = false;
            await using var connection = await ConnectionMultiplexer.ConnectAsync(configuration);
            var latency = await connection.GetDatabase().PingAsync();

            return HealthCheckResult.Healthy(
                "The SignalR Redis backplane is reachable.",
                new Dictionary<string, object> { ["latencyMilliseconds"] = latency.TotalMilliseconds });
        }
        catch (Exception exception) when (exception is RedisException or InvalidOperationException)
        {
            return HealthCheckResult.Unhealthy(
                "The SignalR Redis backplane is unavailable.",
                exception);
        }
    }
}
