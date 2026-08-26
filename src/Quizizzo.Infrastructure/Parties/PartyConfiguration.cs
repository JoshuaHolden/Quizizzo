using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Quizizzo.Domain.Parties;
using Quizizzo.Infrastructure.Identity;

namespace Quizizzo.Infrastructure.Parties;

internal sealed class PartyConfiguration : IEntityTypeConfiguration<Party>
{
    public void Configure(EntityTypeBuilder<Party> builder)
    {
        builder.ToTable("Parties");
        builder.HasKey(party => party.Id);
        builder.Property(party => party.Id)
            .HasConversion(id => id.Value, value => new PartyId(value))
            .ValueGeneratedNever();
        builder.Property(party => party.HostUserId).HasMaxLength(450).IsRequired();
        builder.Property(party => party.RoomCode)
            .HasConversion(code => code.Value, value => RoomCode.Parse(value))
            .HasMaxLength(RoomCode.Length)
            .IsFixedLength()
            .IsRequired();
        builder.Property(party => party.Status).HasConversion<int>();
        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(party => party.HostUserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(party => party.HostUserId)
            .IsUnique()
            .HasFilter("\"Status\" IN (0, 1, 2, 3)");
        builder.HasIndex(party => party.CreatedAt);
        builder.HasIndex(party => party.RoomCode)
            .IsUnique()
            .HasFilter("\"Status\" IN (0, 1, 2, 3)");
    }
}
