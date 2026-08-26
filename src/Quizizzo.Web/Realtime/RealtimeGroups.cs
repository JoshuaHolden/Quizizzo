namespace Quizizzo.Web.Realtime;

public static class RealtimeGroups
{
    public static string Party(Guid partyId) => $"party:{partyId:N}";
    public static string Hosts(Guid partyId) => $"party:{partyId:N}:hosts";
    public static string Players(Guid partyId) => $"party:{partyId:N}:players";
    public static string Displays(Guid partyId) => $"party:{partyId:N}:display";
    public static string DisplaySession(Guid displaySessionId) => $"display-session:{displaySessionId:N}";
}
