using Microsoft.Extensions.Options;
using Quizizzo.Application.Players;

namespace Quizizzo.Web.Realtime;

public sealed partial class PartyConnectionRegistry(
    IServiceScopeFactory scopeFactory,
    IPartyRealtimeNotifier notifier,
    IOptions<RealtimePresenceOptions> options,
    ILogger<PartyConnectionRegistry> logger)
{
    private readonly object gate = new();
    private readonly Dictionary<string, ConnectionBinding> connections = new(StringComparer.Ordinal);
    private readonly Dictionary<PresenceKey, HashSet<string>> subjectConnections = [];
    private readonly Dictionary<PresenceKey, CancellationTokenSource> pendingDisconnects = [];

    public async Task RegisterAsync(
        string connectionId,
        Guid? partyId,
        RealtimeRole role,
        string subjectId,
        CancellationToken cancellationToken)
    {
        var key = new PresenceKey(partyId, role, subjectId);
        var becamePresent = false;

        lock (gate)
        {
            RemoveConnectionWithoutDisconnect(connectionId);
            connections[connectionId] = new ConnectionBinding(connectionId, partyId, role, subjectId);
            if (!subjectConnections.TryGetValue(key, out var connectionIds))
            {
                connectionIds = [];
                subjectConnections[key] = connectionIds;
            }

            becamePresent = connectionIds.Count == 0;
            connectionIds.Add(connectionId);
            if (pendingDisconnects.Remove(key, out var pending))
            {
                pending.Cancel();
                pending.Dispose();
            }
        }

        if (becamePresent && partyId.HasValue)
        {
            await notifier.PartyChangedAsync(partyId.Value, $"{role}Connected", cancellationToken);
        }
    }

    public Task UnregisterAsync(string connectionId)
    {
        ConnectionBinding? binding;
        var becameAbsent = false;
        PresenceKey key = default;

        lock (gate)
        {
            if (!connections.Remove(connectionId, out binding))
            {
                return Task.CompletedTask;
            }

            key = new PresenceKey(binding.PartyId, binding.Role, binding.SubjectId);
            if (subjectConnections.TryGetValue(key, out var connectionIds))
            {
                connectionIds.Remove(connectionId);
                becameAbsent = connectionIds.Count == 0;
                if (becameAbsent)
                {
                    subjectConnections.Remove(key);
                }
            }
        }

        if (!becameAbsent || !binding.PartyId.HasValue)
        {
            return Task.CompletedTask;
        }

        if (binding.Role == RealtimeRole.Player)
        {
            SchedulePlayerDisconnect(key, binding);
            return Task.CompletedTask;
        }

        return notifier.PartyChangedAsync(binding.PartyId.Value, $"{binding.Role}Disconnected");
    }

    public PartyPresenceSnapshot GetSnapshot(Guid partyId)
    {
        lock (gate)
        {
            var active = subjectConnections.Keys.Where(key => key.PartyId == partyId).ToArray();
            return new PartyPresenceSnapshot(
                active.Count(key => key.Role == RealtimeRole.Host),
                active.Count(key => key.Role == RealtimeRole.Player),
                active.Count(key => key.Role == RealtimeRole.Display));
        }
    }

    private void SchedulePlayerDisconnect(PresenceKey key, ConnectionBinding binding)
    {
        var cancellation = new CancellationTokenSource();
        lock (gate)
        {
            if (subjectConnections.ContainsKey(key))
            {
                cancellation.Dispose();
                return;
            }

            if (pendingDisconnects.Remove(key, out var existing))
            {
                existing.Cancel();
                existing.Dispose();
            }
            pendingDisconnects[key] = cancellation;
        }

        _ = MarkPlayerDisconnectedAfterGraceAsync(key, binding, cancellation);
    }

    private async Task MarkPlayerDisconnectedAfterGraceAsync(
        PresenceKey key,
        ConnectionBinding binding,
        CancellationTokenSource cancellation)
    {
        try
        {
            await Task.Delay(options.Value.PlayerDisconnectGracePeriod, cancellation.Token);
            lock (gate)
            {
                if (subjectConnections.ContainsKey(key))
                {
                    return;
                }
            }

            using var scope = scopeFactory.CreateScope();
            var players = scope.ServiceProvider.GetRequiredService<PlayerService>();
            await players.MarkDisconnectedAsync(Guid.Parse(binding.SubjectId), cancellation.Token);
            if (binding.PartyId.HasValue)
            {
                await notifier.PartyChangedAsync(binding.PartyId.Value, "PlayerDisconnected", cancellation.Token);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            LogDisconnectFailure(
                logger,
                exception,
                binding.SubjectId,
                binding.PartyId);
        }
        finally
        {
            lock (gate)
            {
                if (pendingDisconnects.TryGetValue(key, out var current) && ReferenceEquals(current, cancellation))
                {
                    pendingDisconnects.Remove(key);
                }
            }
            cancellation.Dispose();
        }
    }

    private void RemoveConnectionWithoutDisconnect(string connectionId)
    {
        if (!connections.Remove(connectionId, out var oldBinding))
        {
            return;
        }

        var oldKey = new PresenceKey(oldBinding.PartyId, oldBinding.Role, oldBinding.SubjectId);
        if (subjectConnections.TryGetValue(oldKey, out var connectionIds))
        {
            connectionIds.Remove(connectionId);
            if (connectionIds.Count == 0)
            {
                subjectConnections.Remove(oldKey);
            }
        }
    }

    private sealed record ConnectionBinding(string ConnectionId, Guid? PartyId, RealtimeRole Role, string SubjectId);
    private readonly record struct PresenceKey(Guid? PartyId, RealtimeRole Role, string SubjectId);

    [LoggerMessage(
        EventId = 3201,
        Level = LogLevel.Error,
        Message = "Failed to mark player {PlayerId} disconnected from party {PartyId}")]
    private static partial void LogDisconnectFailure(
        ILogger logger,
        Exception exception,
        string playerId,
        Guid? partyId);
}
