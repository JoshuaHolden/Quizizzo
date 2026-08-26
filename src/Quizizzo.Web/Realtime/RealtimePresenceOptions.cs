namespace Quizizzo.Web.Realtime;

public sealed class RealtimePresenceOptions
{
    public const string SectionName = "RealtimePresence";
    public TimeSpan PlayerDisconnectGracePeriod { get; set; } = TimeSpan.FromSeconds(20);
}
