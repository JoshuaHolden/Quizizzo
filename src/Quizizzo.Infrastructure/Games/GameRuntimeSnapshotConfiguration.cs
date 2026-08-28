using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Quizizzo.Infrastructure.Games;

internal sealed class GameRuntimeSnapshotConfiguration
    : IEntityTypeConfiguration<GameRuntimeSnapshotRecord>
{
    public void Configure(EntityTypeBuilder<GameRuntimeSnapshotRecord> builder)
    {
        builder.ToTable("GameRuntimeSnapshots");
        builder.HasKey(snapshot => snapshot.GameInstanceId);
        builder.Property(snapshot => snapshot.GameInstanceId).ValueGeneratedNever();
        builder.Property(snapshot => snapshot.GameKey).HasMaxLength(64).IsRequired();
        builder.Property(snapshot => snapshot.SnapshotJson).HasColumnType("jsonb").IsRequired();
        builder.HasIndex(snapshot => snapshot.PartyId);
        builder.HasIndex(snapshot => new { snapshot.IsComplete, snapshot.UpdatedAtUtc });
    }
}
