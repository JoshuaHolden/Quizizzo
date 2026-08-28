namespace Quizizzo.Web.Security;

internal static class RequestPartitionKey
{
    public static string RemoteAddress(HttpContext context) =>
        $"ip:{context.Connection.RemoteIpAddress?.ToString() ?? "unknown"}";
}
