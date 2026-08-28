using Quizizzo.Domain.Parties;

namespace Quizizzo.Application.Parties;

/// <summary>
/// Serializes short party-level operations that must not race before they enter
/// the game actor, such as admitting a player and starting a game.
/// </summary>
public sealed class PartyMutationCoordinator
{
    private readonly object gate = new();
    private readonly Dictionary<PartyId, Entry> entries = [];

    public async ValueTask<IAsyncDisposable> AcquireAsync(
        PartyId partyId,
        CancellationToken cancellationToken = default)
    {
        Entry entry;
        lock (gate)
        {
            if (!entries.TryGetValue(partyId, out entry!))
            {
                entry = new Entry();
                entries.Add(partyId, entry);
            }
            entry.References += 1;
        }

        try
        {
            await entry.Semaphore.WaitAsync(cancellationToken);
            return new Lease(this, partyId, entry);
        }
        catch
        {
            ReleaseReference(partyId, entry, releaseSemaphore: false);
            throw;
        }
    }

    private void Release(PartyId partyId, Entry entry) =>
        ReleaseReference(partyId, entry, releaseSemaphore: true);

    private void ReleaseReference(PartyId partyId, Entry entry, bool releaseSemaphore)
    {
        if (releaseSemaphore)
        {
            entry.Semaphore.Release();
        }

        var dispose = false;
        lock (gate)
        {
            entry.References -= 1;
            if (entry.References == 0)
            {
                entries.Remove(partyId);
                dispose = true;
            }
        }
        if (dispose)
        {
            entry.Semaphore.Dispose();
        }
    }

    private sealed class Entry
    {
        public SemaphoreSlim Semaphore { get; } = new(1, 1);
        public int References { get; set; }
    }

    private sealed class Lease(
        PartyMutationCoordinator owner,
        PartyId partyId,
        Entry entry) : IAsyncDisposable
    {
        private PartyMutationCoordinator? owner = owner;

        public ValueTask DisposeAsync()
        {
            Interlocked.Exchange(ref owner, null)?.Release(partyId, entry);
            return ValueTask.CompletedTask;
        }
    }
}
