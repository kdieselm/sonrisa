using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sonrisa.Domain.Entities;

namespace Sonrisa.Infrastructure.Persistence.Configurations;

public sealed class AlertConfiguration : IEntityTypeConfiguration<Alert>
{
    public void Configure(EntityTypeBuilder<Alert> builder)
    {
        builder.ToTable("Alerts");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).UseIdentityAlwaysColumn();
        builder.Property(a => a.UserId).HasMaxLength(450).IsRequired();
        builder.Property(a => a.Name).HasMaxLength(Alert.MaxNameLength).IsRequired();
        builder.Property(a => a.CategoryKey).HasMaxLength(64).IsRequired();

        // Shape owned by the category (ADR-2); a string mapped to jsonb, so core code never reads it.
        builder.Property(a => a.Rule).HasColumnType("jsonb").IsRequired();

        // Owner: the account; no cascade, alerts are soft-deleted and their history stays (D20).
        builder.HasOne<AppUser>().WithMany().HasForeignKey(a => a.UserId).OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(a => a.Destinations).WithOne().HasForeignKey(d => d.AlertId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(a => a.Destinations).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Ignore(a => a.IsDeleted);

        // State of a stateful category (market): its own table, so matching and the owner never write the same row.
        builder.HasOne(a => a.State).WithOne().HasForeignKey<AlertState>(s => s.AlertId)
            .OnDelete(DeleteBehavior.Cascade);

        // Matching loads the enabled alerts of one category (architecture §4); the user list filters by owner.
        builder.HasIndex(a => new { a.CategoryKey, a.Enabled });
        builder.HasIndex(a => a.UserId);
    }
}

public sealed class AlertStateConfiguration : IEntityTypeConfiguration<AlertState>
{
    public void Configure(EntityTypeBuilder<AlertState> builder)
    {
        builder.ToTable("AlertStates");
        builder.HasKey(s => s.AlertId);
        builder.Property(s => s.AlertId).ValueGeneratedNever();

        // Shape owned by the category (ADR-2, ADR-5); a string mapped to jsonb, never read by core code.
        builder.Property(s => s.State).HasColumnType("jsonb").IsRequired();
    }
}

public sealed class AlertDestinationConfiguration : IEntityTypeConfiguration<AlertDestination>
{
    public void Configure(EntityTypeBuilder<AlertDestination> builder)
    {
        builder.ToTable("AlertDestinations");
        builder.HasKey(d => new { d.AlertId, d.DestinationId });
        builder.HasOne<Destination>().WithMany().HasForeignKey(d => d.DestinationId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(d => d.DestinationId);
    }
}
