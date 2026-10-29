using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sonrisa.Domain.Entities;

namespace Sonrisa.Infrastructure.Persistence.Configurations;

public sealed class DestinationConfiguration : IEntityTypeConfiguration<Destination>
{
    public void Configure(EntityTypeBuilder<Destination> builder)
    {
        builder.ToTable("Destinations");
        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id).UseIdentityAlwaysColumn();
        builder.Property(d => d.UserId).HasMaxLength(450).IsRequired();
        builder.Property(d => d.ChannelKey).HasMaxLength(64).IsRequired();
        builder.Property(d => d.DisplayName).HasMaxLength(Destination.MaxDisplayNameLength).IsRequired();

        // Shape owned by the channel (ADR-2). May hold a secret (Slack webhook URL): only the channel's mask leaves
        // the API (AC18).
        builder.Property(d => d.Config).HasColumnType("jsonb").IsRequired();

        builder.HasOne<AppUser>().WithMany().HasForeignKey(d => d.UserId).OnDelete(DeleteBehavior.Restrict);
        builder.Ignore(d => d.IsDeleted);
        builder.HasIndex(d => d.UserId);
    }
}
