using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sonrisa.Api.Configuration;
using Sonrisa.Application;
using Sonrisa.Application.Services;
using Sonrisa.Contracts.Dtos;
using Sonrisa.Contracts.Dtos.Events;
using Sonrisa.Domain.Entities;
using System.Security.Claims;

namespace Sonrisa.Api.Controllers.Admin;

/// <summary>Admin view of ingested events and the "simulate event" form (spec §2.3, §2.9).</summary>
[ApiController]
[Route("api/admin/events")]
[Authorize(Roles = AdminSeeding.AdminRole)]
public sealed class AdminEventsController(EventAdminService events, AuditService audit,
    ILogger<AdminEventsController> logger) : ControllerBase
{
    /// <summary>Newest first, optionally filtered by category and source key.</summary>
    [HttpGet]
    public Task<PagedResult<AdminEventDto>> List([FromQuery] string? category, [FromQuery] string? source,
        [FromQuery] int page = 1, [FromQuery] int pageSize = Paging.DefaultPageSize, CancellationToken ct = default) =>
        events.ListAsync(category, source, page, pageSize, ct);

    [HttpGet("filters")]
    public EventFiltersDto Filters() => events.Filters();

    /// <summary>Creates a simulated event through the normal ingestion path; audited (spec §2.10).</summary>
    [HttpPost("simulate")]
    public async Task<ActionResult<AdminEventDto>> Simulate(SimulateEventRequest request, CancellationToken ct)
    {
        var (created, errors) = await events.SimulateAsync(request, ct);
        if (created is null)
        {
            return ValidationProblem(new ValidationProblemDetails(
                errors!.ToDictionary(e => e.Key, e => e.Value)));
        }

        // The event is committed at this point; a failed audit write is logged, not turned into an error.
        try
        {
            await audit.RecordAsync(User.FindFirstValue(ClaimTypes.NameIdentifier), AuditActions.SimulateEvent,
                AuditTargets.Event, created.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                AuditOutcome.Success, CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not write the audit entry for simulated event {EventId}.", created.Id);
        }

        return StatusCode(StatusCodes.Status201Created, created);
    }
}
