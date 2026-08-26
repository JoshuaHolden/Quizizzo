using Quizizzo.Domain.Displays;

namespace Quizizzo.Application.Abstractions;

public interface IDisplaySessionRepository
{
    Task<bool> PairingCodeExistsAsync(string pairingCode, CancellationToken cancellationToken);
    Task AddAsync(DisplaySession displaySession, CancellationToken cancellationToken);
    Task<DisplaySession?> GetByIdAsync(DisplaySessionId displaySessionId, CancellationToken cancellationToken);
    Task<DisplaySession?> GetBySessionTokenHashAsync(string tokenHash, CancellationToken cancellationToken);
    Task<DisplaySession?> GetByPairingCodeAsync(string pairingCode, CancellationToken cancellationToken);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
