using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Quizizzo.Domain.Parties;
using Quizizzo.Domain.Players;
using Quizizzo.Domain;

namespace Quizizzo.Infrastructure.Players;

internal sealed class PlayerConfiguration : IEntityTypeConfiguration<Player>
{
    public void Configure(EntityTypeBuilder<Player> builder)
    {
        builder.ToTable("Players");
        builder.HasKey(player => player.Id);
        builder.Property(player => player.Id)
            .HasConversion(id => id.Value, value => new PlayerId(value))
            .ValueGeneratedNever();
        builder.Property(player => player.PartyId)
            .HasConversion(id => id.Value, value => new PartyId(value));
        builder.Property(player => player.DisplayName)
            .HasConversion(name => name.Value, value => PlayerName.Parse(value))
            .HasMaxLength(QuizizzoLimits.PlayerNameLength)
            .IsRequired();
        builder.Property(player => player.Status).HasConversion<int>();
        builder.Property(player => player.SessionTokenHash)
            .HasMaxLength(64)
            .IsFixedLength()
            .IsRequired();
        builder.OwnsOne(player => player.Character, character =>
        {
            character.Property(value => value.BodyType).HasColumnName("CharacterBodyType").HasConversion<int>();
            character.Property(value => value.PrimaryColour).HasColumnName("CharacterPrimaryColour").HasMaxLength(7).IsRequired();
            character.Property(value => value.Eyes).HasColumnName("CharacterEyes").HasConversion<int>();
            character.Property(value => value.Mouth).HasColumnName("CharacterMouth").HasConversion<int>();
            character.Property(value => value.Accessory).HasColumnName("CharacterAccessory").HasConversion<int>();
        });
        builder.HasOne<Party>()
            .WithMany()
            .HasForeignKey(player => player.PartyId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(player => player.SessionTokenHash).IsUnique();
        builder.HasIndex(player => new { player.PartyId, player.Status });
        builder.HasIndex(player => player.LastSeenAt);
        builder.HasIndex(player => player.JoinedAt);
    }
}
