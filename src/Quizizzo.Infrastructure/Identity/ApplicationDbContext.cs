using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Quizizzo.Domain.Displays;
using Quizizzo.Domain.Parties;
using Quizizzo.Domain.Players;

namespace Quizizzo.Infrastructure.Identity;

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<Party> Parties => Set<Party>();
    public DbSet<DisplaySession> DisplaySessions => Set<DisplaySession>();
    public DbSet<Player> Players => Set<Player>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);
    }
}
