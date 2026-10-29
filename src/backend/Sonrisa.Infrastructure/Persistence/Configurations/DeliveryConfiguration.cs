using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sonrisa.Domain.Entities;

namespace Sonrisa.Infrastructure.Persistence.Configurations;

public sealed class DeliveryConfiguration : IEntityTypeConfiguration<Delivery>
{
    public void Configure(EntityTypeBuilder<Delivery> builder)
    {
        builder.ToTable("Deliveries");
        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id).UseIdentityAlwaysColumn();
        builder.Property(d => d.Status).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(d => d.Failure).HasConversion<string>().HasMaxLength(32);

        // Optimistic concurrency on the PostgreSQL row version: the worker holds a tracked row across a send, while a
        // delete may fail the same row with a set-based update (S4 review 3).
        builder.Property<uint>("xmin").HasColumnType("xid").ValueGeneratedOnAddOrUpdate().IsConcurrencyToken();
        builder.Property(d => d.LastError).HasMaxLength(Delivery.MaxErrorLength);

        // History is never cascaded away (architecture §4): alerts and destinations are soft-deleted.
        builder.HasOne<Alert>().WithMany().HasForeignKey(d => d.AlertId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(d => d.Event).WithMany().HasForeignKey(d => d.EventId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Destination>().WithMany().HasForeignKey(d => d.DestinationId)
            .OnDelete(DeleteBehavior.Restrict);

        // No double delivery (D16, AC13): one row per alert, event and destination.
        builder.HasIndex(d => new { d.AlertId, d.EventId, d.DestinationId }).IsUnique();

        // Worker: due rows (S4). The Notifications page reaches a user's rows through the alert (AlertId index above).
        builder.HasIndex(d => new { d.Status, d.NextAttemptAt });
        builder.HasIndex(d => d.DestinationId);
        builder.HasIndex(d => d.EventId);
    }
}
