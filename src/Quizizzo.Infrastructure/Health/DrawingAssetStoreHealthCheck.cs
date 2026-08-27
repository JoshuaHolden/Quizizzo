using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Quizizzo.Infrastructure.Drawings;

namespace Quizizzo.Infrastructure.Health;

public sealed class DrawingAssetStoreHealthCheck(
    IOptions<DrawingAssetStoreOptions> options) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var rootPath = Path.GetFullPath(options.Value.RootPath);
            Directory.CreateDirectory(rootPath);
            var probePath = Path.Combine(rootPath, $".health-{Guid.NewGuid():N}.tmp");
            try
            {
                await using var stream = new FileStream(
                    probePath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    1,
                    FileOptions.Asynchronous | FileOptions.DeleteOnClose);
                await stream.WriteAsync(new byte[] { 0x01 }, cancellationToken);
            }
            finally
            {
                if (File.Exists(probePath))
                {
                    File.Delete(probePath);
                }
            }
            return HealthCheckResult.Healthy();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                                           InvalidOperationException or ArgumentException)
        {
            return HealthCheckResult.Unhealthy("Drawing asset storage is unavailable.", exception);
        }
    }
}
