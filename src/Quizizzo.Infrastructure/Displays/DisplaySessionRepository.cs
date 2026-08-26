using Microsoft.EntityFrameworkCore;
using Quizizzo.Application.Abstractions;
using Quizizzo.Domain.Displays;
using Quizizzo.Infrastructure.Identity;

namespace Quizizzo.Infrastructure.Displays;

public sealed class DisplaySessionRepository(ApplicationDbContext dbContext) : IDisplaySessionRepository
{
    public Task<bool> PairingCodeExistsAsync(string pairingCode, CancellationToken cancellationToken) =>
        dbContext.DisplaySessions.AnyAsync(session => session.PairingCode == pairingCode, cancellationToken);

    public async Task AddAsync(DisplaySession displaySession, CancellationToken cancellationToken) =>
        await dbContext.DisplaySessions.AddAsync(displaySession, cancellationToken);

    public Task<DisplaySession?> GetByIdAsync(
        DisplaySessionId displaySessionId,
        CancellationToken cancellationToken) =>
        dbContext.DisplaySessions.SingleOrDefaultAsync(
            session => session.Id == displaySessionId, cancellationToken);

    public Task<DisplaySession?> GetBySessionTokenHashAsync(string tokenHash, CancellationToken cancellationToken) =>
        dbContext.DisplaySessions.SingleOrDefaultAsync(
            session => session.SessionTokenHash == tokenHash, cancellationToken);

    public Task<DisplaySession?> GetByPairingCodeAsync(string pairingCode, CancellationToken cancellationToken) =>
        dbContext.DisplaySessions.SingleOrDefaultAsync(
            session => session.PairingCode == pairingCode, cancellationToken);

    public async Task SaveChangesAsync(CancellationToken cancellationToken) =>
        await dbContext.SaveChangesAsync(cancellationToken);
}
