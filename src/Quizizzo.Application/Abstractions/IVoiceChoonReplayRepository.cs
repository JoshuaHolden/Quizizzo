using Quizizzo.Domain.Voice;

namespace Quizizzo.Application.Abstractions;

public interface IVoiceChoonReplayRepository
{
    Task<VoiceChoonReplay?> GetByGameInstanceAsync(Guid gameInstanceId, CancellationToken cancellationToken = default);
    Task<VoiceChoonReplay?> GetByShareCodeAsync(string shareCode, CancellationToken cancellationToken = default);
    Task AddAsync(VoiceChoonReplay replay, CancellationToken cancellationToken = default);
    Task DeleteAsync(VoiceChoonReplay replay, CancellationToken cancellationToken = default);
}
