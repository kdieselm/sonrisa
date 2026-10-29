using System.Text.Json;
using Sonrisa.Application.Abstractions;
using Sonrisa.Application.Registries;
using Sonrisa.Contracts.Dtos;
using Sonrisa.Contracts.Dtos.Events;
using Sonrisa.Domain.Categories;
using Sonrisa.Domain.Entities;

namespace Sonrisa.Application.Services;

/// <summary>Admin event list, its filter choices, and the "simulate event" form (spec §2.3, §2.9).</summary>
public sealed class EventAdminService(
    IEventRepository events,
    EventIngestor ingestor,
    CategoryRegistry categories,
    SourceRegistry sources)
{
    public const int MaxFilterLength = 64;

    public async Task<PagedResult<AdminEventDto>> ListAsync(string? categoryKey, string? sourceKey, int page,
        int pageSize, CancellationToken ct)
    {
        (page, pageSize) = Paging.Normalize(page, pageSize);
        var (items, total) = await events.ListAsync(Blank(categoryKey), Blank(sourceKey), page, pageSize, ct);
        return new PagedResult<AdminEventDto>(items.Select(ToDto).ToList(), page, pageSize, total);
    }

    public EventFiltersDto Filters()
    {
        var categoryOptions = categories.All
            .Select(c => new CategoryOptionDto(c.Key, c.DisplayName))
            .OrderBy(c => c.Name, StringComparer.Ordinal)
            .ToList();
        var sourceOptions = sources.All
            .Select(s => new SourceOptionDto(s.Key, s.DisplayName, s.CategoryKey))
            .OrderBy(s => s.Name, StringComparer.Ordinal)
            .Append(new SourceOptionDto(SourceRegistry.SimulatedKey,
                sources.DisplayNameOf(SourceRegistry.SimulatedKey), null))
            .ToList();
        return new EventFiltersDto(categoryOptions, sourceOptions);
    }

    /// <summary>
    /// Creates a simulated event (spec §2.3, T14): source <c>simulated</c>, a new external ID, published and first
    /// seen now, then the same ingestion path as a polled item. Returns field errors instead when the input is invalid.
    /// </summary>
    public async Task<(AdminEventDto? Event, IReadOnlyDictionary<string, string[]>? Errors)> SimulateAsync(
        SimulateEventRequest request, CancellationToken ct)
    {
        var errors = new FieldErrors();
        var category = request.CategoryKey is null ? null : categories.Find(request.CategoryKey);
        if (category is null)
        {
            errors.Add("categoryKey", "Choose a known category.");
        }

        var title = request.Title?.Trim();
        if (string.IsNullOrEmpty(title) || title.Length > Event.MaxTitleLength)
        {
            errors.Add("title", $"Title is required, at most {Event.MaxTitleLength} characters.");
        }

        var summary = string.IsNullOrWhiteSpace(request.Summary) ? null : request.Summary.Trim();
        if (summary?.Length > Event.MaxSummaryLength)
        {
            errors.Add("summary", $"Summary is at most {Event.MaxSummaryLength} characters.");
        }

        var url = string.IsNullOrWhiteSpace(request.Url) ? null : request.Url.Trim();
        if (url is not null && (url.Length > Event.MaxUrlLength || !IsHttpUrl(url)))
        {
            errors.Add("url", "Link must be an absolute http or https URL.");
        }

        var data = category?.ValidateSimulatedData(request.Data);
        foreach (var (field, messages) in data?.Errors ?? new Dictionary<string, string[]>())
        {
            foreach (var message in messages)
            {
                errors.Add($"data.{field}", message);
            }
        }

        if (errors.Any || category is null || data?.DataJson is null)
        {
            return (null, errors.ToDictionary());
        }

        var item = new IncomingEvent(Guid.NewGuid().ToString("N"), title!, summary, url, null, data.DataJson);
        var result = await ingestor.IngestAsync(SourceRegistry.SimulatedKey, category.Key, item, isSimulated: true, ct);
        return (ToDto(result.Event), null);
    }

    private AdminEventDto ToDto(Event e)
    {
        using var data = JsonDocument.Parse(e.Data);
        return new AdminEventDto(
            e.Id,
            e.CategoryKey,
            categories.Find(e.CategoryKey)?.DisplayName ?? e.CategoryKey,
            e.SourceKey,
            sources.DisplayNameOf(e.SourceKey),
            e.ExternalId,
            e.Title,
            e.Summary,
            e.Url,
            e.PublishedAt,
            e.FirstSeenAt,
            e.UpdatedAt,
            e.IsSimulated,
            data.RootElement.Clone());
    }

    private static string? Blank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim()[..Math.Min(value.Trim().Length, MaxFilterLength)];

    private static bool IsHttpUrl(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
}
