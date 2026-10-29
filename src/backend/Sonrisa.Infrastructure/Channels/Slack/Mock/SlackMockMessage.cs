using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sonrisa.Application;
using Sonrisa.Application.Abstractions;
using Sonrisa.Infrastructure.Persistence;

namespace Sonrisa.Infrastructure.Channels.Slack.Mock;

/// <summary>
/// One message the local Slack mock receiver accepted (spec §2.6). Owned by the Slack channel, not by the core model
/// (architecture §4): its table comes from the assembly scan of entity configurations, so it needs a migration but no
/// DbContext change.
/// </summary>
public sealed class SlackMockMessage
{
    public const int MaxHookNameLength = 64;
    public const int MaxBodyLength = 16_000;

    /// <summary>The mock keeps only the newest messages, so an anonymous caller cannot fill the database.</summary>
    public const int MaxStored = 500;

    public long Id { get; private set; }
    public string HookName { get; private set; } = string.Empty;
    public string Body { get; private set; } = string.Empty;
    public DateTimeOffset ReceivedAt { get; private set; }

    private SlackMockMessage()
    {
    }

    public SlackMockMessage(string hookName, string body, DateTimeOffset receivedAt)
    {
        HookName = hookName;
        Body = body.Length <= MaxBodyLength ? body : body[..MaxBodyLength];
        ReceivedAt = new DateTimeOffset(receivedAt.UtcTicks - receivedAt.UtcTicks % 10, TimeSpan.Zero);
    }
}

public sealed class SlackMockMessageConfiguration : IEntityTypeConfiguration<SlackMockMessage>
{
    public void Configure(EntityTypeBuilder<SlackMockMessage> builder)
    {
        builder.ToTable("SlackMockMessages");
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).UseIdentityAlwaysColumn();
        builder.Property(m => m.HookName).HasMaxLength(SlackMockMessage.MaxHookNameLength).IsRequired();
        builder.Property(m => m.Body).HasMaxLength(SlackMockMessage.MaxBodyLength).IsRequired();
        builder.HasIndex(m => m.ReceivedAt);
    }
}

public sealed class SlackMockStore(AppDbContext db, TimeProvider time) : ISlackMockStore
{
    public async Task AddAsync(string hookName, string body, CancellationToken ct)
    {
        var message = new SlackMockMessage(hookName, body, time.GetUtcNow());
        db.Set<SlackMockMessage>().Add(message);
        await db.SaveChangesAsync(ct);

        // Identity IDs only grow, so everything older than the newest MaxStored IDs can go.
        var cutoff = message.Id - SlackMockMessage.MaxStored;
        if (cutoff > 0)
        {
            await db.Set<SlackMockMessage>().Where(m => m.Id <= cutoff).ExecuteDeleteAsync(ct);
        }
    }

    public async Task<(IReadOnlyList<SlackMockMessageRecord> Items, int Total)> ListAsync(int page, int pageSize,
        CancellationToken ct)
    {
        var query = db.Set<SlackMockMessage>().AsNoTracking();
        var total = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(m => m.ReceivedAt)
            .ThenByDescending(m => m.Id)
            .Skip(Paging.Offset(page, pageSize))
            .Take(pageSize)
            .Select(m => new SlackMockMessageRecord(m.Id, m.HookName, m.Body, m.ReceivedAt))
            .ToListAsync(ct);
        return (items, total);
    }
}
