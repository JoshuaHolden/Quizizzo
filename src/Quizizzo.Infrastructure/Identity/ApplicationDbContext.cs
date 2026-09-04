using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Quizizzo.Domain.Displays;
using Quizizzo.Domain.Parties;
using Quizizzo.Domain.Players;
using Quizizzo.Domain.Drawings;
using Quizizzo.Domain.Voice;
using Quizizzo.Infrastructure.Games;

namespace Quizizzo.Infrastructure.Identity;

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<Party> Parties => Set<Party>();
    public DbSet<DisplaySession> DisplaySessions => Set<DisplaySession>();
    public DbSet<Player> Players => Set<Player>();
    public DbSet<DrawingAssetMetadata> DrawingAssets => Set<DrawingAssetMetadata>();
    public DbSet<VoiceSampleMetadata> VoiceSamples => Set<VoiceSampleMetadata>();
    public DbSet<VoiceChoonSong> VoiceChoonSongs => Set<VoiceChoonSong>();
    internal DbSet<GameRuntimeSnapshotRecord> GameRuntimeSnapshots => Set<GameRuntimeSnapshotRecord>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);
    }
}
