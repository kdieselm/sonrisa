using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sonrisa.Domain.Entities;

namespace Sonrisa.Infrastructure.Persistence.Configurations;

public sealed class AuditEntryConfiguration : IEntityTypeConfiguration<AuditEntry>
{
    public void Configure(EntityTypeBuilder<AuditEntry> builder)
    {
        builder.ToTable("AuditEntries");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).UseIdentityAlwaysColumn();
        builder.Property(e => e.ActorUserId).HasMaxLength(450);
        builder.Property(e => e.Action).HasMaxLength(64).IsRequired();
        builder.Property(e => e.TargetType).HasMaxLength(64);
        builder.Property(e => e.TargetId).HasMaxLength(450);
        builder.Property(e => e.Outcome).HasConversion<string>().HasMaxLength(16);

        // Newest-first paging in the admin view; actor lookups in the tests and later per-user views.
        builder.HasIndex(e => e.At);
        builder.HasIndex(e => e.ActorUserId);
    }
}
