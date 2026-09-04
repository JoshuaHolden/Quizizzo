using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Quizizzo.Domain.Voice;

namespace Quizizzo.Infrastructure.Voice;

internal sealed class VoiceSampleMetadataConfiguration : IEntityTypeConfiguration<VoiceSampleMetadata>
{
    public void Configure(EntityTypeBuilder<VoiceSampleMetadata> builder)
    {
        builder.ToTable("VoiceSamples");
        builder.HasKey(sample => sample.Id);
        builder.Property(sample => sample.Id).ValueGeneratedNever();
        builder.Property(sample => sample.PromptKey).HasMaxLength(128).IsRequired();
        builder.Property(sample => sample.StorageKey).HasMaxLength(64).IsRequired();
        builder.Property(sample => sample.ContentType).HasMaxLength(32).IsRequired();
        builder.HasIndex(sample => sample.ExpiresAtUtc);
        builder.HasIndex(sample => new
        {
            sample.SubmissionId,
            sample.GameInstanceId,
            sample.PlayerId,
            sample.PromptKey
        }).IsUnique();
    }
}