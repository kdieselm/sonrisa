using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sonrisa.Domain.Entities;

namespace Sonrisa.Infrastructure.Persistence.Configurations;

public sealed class EventConfiguration : IEntityTypeConfiguration<Event>
{
    public void Configure(EntityTypeBuilder<Event> builder)
    {
        builder.ToTable("Events");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).UseIdentityAlwaysColumn();
        builder.Property(e => e.CategoryKey).HasMaxLength(64).IsRequired();
        builder.Property(e => e.SourceKey).HasMaxLength(64).IsRequired();
        builder.Property(e => e.ExternalId).HasMaxLength(Event.MaxExternalIdLength).IsRequired();
        builder.Property(e => e.Title).HasMaxLength(Event.MaxTitleLength).IsRequired();
        builder.Property(e => e.Summary).HasMaxLength(Event.MaxSummaryLength);
        builder.Property(e => e.Url).HasMaxLength(Event.MaxUrlLength);

        // Shape owned by the category (ADR-2); a string mapped to jsonb, so core code never reads it.
        builder.Property(e => e.Data).HasColumnType("jsonb").IsRequired();

        // Item dedup (spec §2.3, T31): the last guard against a second row for the same item.
        builder.HasIndex(e => new { e.SourceKey, e.ExternalId }).IsUnique();

        // Admin list: newest first, optionally by category; S3b back-fill: recent events of one category.
        builder.HasIndex(e => e.FirstSeenAt);
        builder.HasIndex(e => new { e.CategoryKey, e.FirstSeenAt });
    }
}
