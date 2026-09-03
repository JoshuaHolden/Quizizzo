using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Quizizzo.Domain.Parties;
using Quizizzo.Infrastructure.Identity;

namespace Quizizzo.Infrastructure.Parties;

internal sealed class PartyConfiguration : IEntityTypeConfiguration<Party>
{
    private static readonly JsonSerializerOptions QueueJsonOptions = new(JsonSerializerDefaults.Web);

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
        builder.Property(party => party.CurrentGameKey).HasMaxLength(64);
        var queueProperty = builder.Property(party => party.GameQueue)
            .HasConversion(
                queue => JsonSerializer.Serialize(queue, QueueJsonOptions),
                json => DeserializeQueue(json))
            .HasColumnType("jsonb")
            .HasDefaultValueSql("'[]'::jsonb")
            .IsRequired();
        queueProperty.Metadata.SetValueComparer(new ValueComparer<IReadOnlyList<PartyGameQueueItem>>(
            (left, right) => QueuesEqual(left, right),
            queue => QueueHashCode(queue),
            queue => queue.ToArray()));
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

    private static PartyGameQueueItem[] DeserializeQueue(string json) =>
        JsonSerializer.Deserialize<PartyGameQueueItem[]>(json, QueueJsonOptions) ?? [];

    private static bool QueuesEqual(
        IReadOnlyList<PartyGameQueueItem>? left,
        IReadOnlyList<PartyGameQueueItem>? right) =>
        ReferenceEquals(left, right) ||
        (left is not null && right is not null && left.SequenceEqual(right));

    private static int QueueHashCode(IReadOnlyList<PartyGameQueueItem> queue)
    {
        var hash = new HashCode();
        foreach (var item in queue)
        {
            hash.Add(item);
        }
        return hash.ToHashCode();
    }
}
