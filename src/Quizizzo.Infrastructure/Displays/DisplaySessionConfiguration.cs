using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Quizizzo.Domain.Displays;
using Quizizzo.Domain.Parties;

namespace Quizizzo.Infrastructure.Displays;

internal sealed class DisplaySessionConfiguration : IEntityTypeConfiguration<DisplaySession>
{
    public void Configure(EntityTypeBuilder<DisplaySession> builder)
    {
        builder.ToTable("DisplaySessions");
        builder.HasKey(session => session.Id);
        builder.Property(session => session.Id)
            .HasConversion(id => id.Value, value => new DisplaySessionId(value))
            .ValueGeneratedNever();
        builder.Property(session => session.SessionTokenHash).HasMaxLength(64).IsFixedLength().IsRequired();
        builder.Property(session => session.PairingCode).HasMaxLength(8).IsFixedLength().IsRequired();
        builder.Property(session => session.PartyId)
            .HasConversion(
                id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue ? new PartyId(value.Value) : null);
        builder.HasOne<Party>()
            .WithMany()
            .HasForeignKey(session => session.PartyId)
            .OnDelete(DeleteBehavior.SetNull);
        builder.HasIndex(session => session.SessionTokenHash).IsUnique();
        builder.HasIndex(session => session.PairingCode).IsUnique();
        builder.HasIndex(session => session.PartyId);
        builder.HasIndex(session => session.CreatedAt);
    }
}
