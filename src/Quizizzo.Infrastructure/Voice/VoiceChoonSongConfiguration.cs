using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Quizizzo.Domain.Voice;

namespace Quizizzo.Infrastructure.Voice;

internal sealed class VoiceChoonSongConfiguration : IEntityTypeConfiguration<VoiceChoonSong>
{
    public void Configure(EntityTypeBuilder<VoiceChoonSong> builder)
    {
        builder.ToTable("VoiceChoonSongs");
        builder.HasKey(song => song.Id);
        builder.Property(song => song.Key).HasMaxLength(64).IsRequired();
        builder.HasIndex(song => song.Key).IsUnique();
        builder.Property(song => song.DisplayName).HasMaxLength(80).IsRequired();
        builder.Property(song => song.FileName).HasMaxLength(128).IsRequired();
        builder.Property(song => song.MidiData).HasColumnType("bytea").IsRequired();
        builder.Property(song => song.CreatedByUserId).HasMaxLength(450).IsRequired();
        builder.HasIndex(song => song.CreatedAtUtc);
    }
}
