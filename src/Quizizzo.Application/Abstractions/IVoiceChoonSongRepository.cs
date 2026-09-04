using Quizizzo.Domain.Voice;

namespace Quizizzo.Application.Abstractions;

public interface IVoiceChoonSongRepository
{
    Task<IReadOnlyList<VoiceChoonSong>> ListAsync(CancellationToken cancellationToken = default);
    Task<VoiceChoonSong?> GetAsync(Guid id, CancellationToken cancellationToken = default);
    Task<bool> KeyExistsAsync(string key, CancellationToken cancellationToken = default);
    Task<bool> IsReferencedAsync(string key, CancellationToken cancellationToken = default);
    Task AddAsync(VoiceChoonSong song, CancellationToken cancellationToken = default);
    Task DeleteAsync(VoiceChoonSong song, CancellationToken cancellationToken = default);
}
