using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Quizizzo.Domain.Drawings;

namespace Quizizzo.Infrastructure.Drawings;

internal sealed class DrawingAssetMetadataConfiguration : IEntityTypeConfiguration<DrawingAssetMetadata>
{
    public void Configure(EntityTypeBuilder<DrawingAssetMetadata> builder)
    {
        builder.ToTable("DrawingAssets");
        builder.HasKey(asset => asset.Id);
        builder.Property(asset => asset.Id).ValueGeneratedNever();
        builder.Property(asset => asset.RoundId).HasMaxLength(128).IsRequired();
        builder.Property(asset => asset.StorageKey).HasMaxLength(64).IsRequired();
        builder.Property(asset => asset.ContentType).HasMaxLength(32).IsRequired();
        builder.HasIndex(asset => asset.ExpiresAtUtc);
        builder.HasIndex(asset => new
        {
            asset.SubmissionId,
            asset.GameInstanceId,
            asset.PlayerId,
            asset.RoundId,
            asset.FrameNumber
        }).IsUnique();
    }
}
