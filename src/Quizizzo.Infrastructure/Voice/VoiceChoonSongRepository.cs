using Microsoft.EntityFrameworkCore;
using Quizizzo.Application.Abstractions;
using Quizizzo.Domain.Voice;
using Quizizzo.Infrastructure.Identity;

namespace Quizizzo.Infrastructure.Voice;

public sealed class VoiceChoonSongRepository(ApplicationDbContext db) : IVoiceChoonSongRepository
{
    public async Task<IReadOnlyList<VoiceChoonSong>> ListAsync(CancellationToken cancellationToken = default) =>
        await db.VoiceChoonSongs.AsNoTracking().OrderBy(song => song.DisplayName).ToArrayAsync(cancellationToken);
    public Task<VoiceChoonSong?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.VoiceChoonSongs.SingleOrDefaultAsync(song => song.Id == id, cancellationToken);
    public Task<bool> KeyExistsAsync(string key, CancellationToken cancellationToken = default) =>
        db.VoiceChoonSongs.AnyAsync(song => song.Key == key, cancellationToken);
    public async Task<bool> IsReferencedAsync(string key, CancellationToken cancellationToken = default)
    {
        var snapshots = await db.GameRuntimeSnapshots.AsNoTracking()
            .Where(snapshot => snapshot.GameKey == "voicechoon" && !snapshot.IsComplete)
            .Select(snapshot => snapshot.SnapshotJson)
            .ToArrayAsync(cancellationToken);
        if (snapshots.Any(snapshot => snapshot.Contains($"\"{key}\"", StringComparison.OrdinalIgnoreCase)))
            return true;
        var parties = await db.Parties.AsNoTracking().ToArrayAsync(cancellationToken);
        return parties.Any(party => party.GameQueue.Any(item => item.GameKey == "voicechoon" &&
            QueueUsesSong(item.ConfigurationJson, key)));
    }
    public async Task AddAsync(VoiceChoonSong song, CancellationToken cancellationToken = default)
    { db.VoiceChoonSongs.Add(song); await db.SaveChangesAsync(cancellationToken); }
    public async Task UpdateAsync(VoiceChoonSong song, CancellationToken cancellationToken = default)
    { db.VoiceChoonSongs.Update(song); await db.SaveChangesAsync(cancellationToken); }
    public async Task DeleteAsync(VoiceChoonSong song, CancellationToken cancellationToken = default)
    { db.VoiceChoonSongs.Remove(song); await db.SaveChangesAsync(cancellationToken); }

    private static bool QueueUsesSong(string json, string key)
    {
        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(json);
            return document.RootElement.TryGetProperty("songKey", out var value) ||
                   document.RootElement.TryGetProperty("SongKey", out value)
                ? string.Equals(value.GetString(), key, StringComparison.OrdinalIgnoreCase)
                : false;
        }
        catch (System.Text.Json.JsonException) { return true; }
    }
}
