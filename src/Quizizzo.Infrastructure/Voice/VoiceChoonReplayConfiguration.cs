using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Quizizzo.Domain.Voice;

namespace Quizizzo.Infrastructure.Voice;

internal sealed class VoiceChoonReplayConfiguration : IEntityTypeConfiguration<VoiceChoonReplay>
{
    public void Configure(EntityTypeBuilder<VoiceChoonReplay> builder)
    {
        builder.ToTable("VoiceChoonReplays");
        builder.HasKey(replay => replay.Id);
        builder.Property(replay => replay.Id).ValueGeneratedNever();
        builder.Property(replay => replay.ShareCode).HasMaxLength(64).IsRequired();
        builder.Property(replay => replay.HostUserId).HasMaxLength(450).IsRequired();
        builder.Property(replay => replay.Title).HasMaxLength(160).IsRequired();
        builder.Property(replay => replay.SnapshotJson).HasColumnType("jsonb").IsRequired();
        builder.Property(replay => replay.SampleAssetIds).HasColumnType("uuid[]").IsRequired();
        builder.HasIndex(replay => replay.ShareCode).IsUnique();
        builder.HasIndex(replay => replay.GameInstanceId).IsUnique();
        builder.HasIndex(replay => replay.HostUserId);
    }
}
