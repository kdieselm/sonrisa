using Microsoft.AspNetCore.Mvc;
using Sonrisa.Application;
using Sonrisa.Application.Services;
using Sonrisa.Contracts.Dtos;
using Sonrisa.Contracts.Dtos.Alerts;
using Sonrisa.Domain.Entities;

namespace Sonrisa.Api.Controllers;

/// <summary>The caller's alerts (spec §2.2, §2.5): the five actions of AC5 and the recent-matches list (AC14).</summary>
[Route("api/alerts")]
public sealed class AlertsController(AlertService alerts, AuditService audit, ILogger<AlertsController> logger)
    : UserControllerBase(audit, logger)
{
    [HttpGet]
    public Task<PagedResult<AlertDto>> List([FromQuery] int page = 1,
        [FromQuery] int pageSize = Paging.DefaultPageSize, CancellationToken ct = default) =>
        alerts.ListAsync(CallerId, page, pageSize, ct);

    [HttpGet("{id:long}")]
    public async Task<ActionResult<AlertDto>> Get(long id, CancellationToken ct) =>
        await alerts.GetAsync(id, CallerId, ct) is { } dto ? dto : NotFound();

    [HttpPost]
    public async Task<ActionResult<AlertDto>> Create(CreateAlertRequest request, CancellationToken ct)
    {
        var result = await alerts.CreateAsync(CallerId, request, ct);
        if (ErrorFor(result) is { } problem)
        {
            return problem;
        }

        await AuditAsync(AuditActions.AlertCreate, AuditTargets.Alert, result.Value!.Id);
        return StatusCode(StatusCodes.Status201Created, result.Value);
    }

    [HttpPut("{id:long}")]
    public async Task<ActionResult<AlertDto>> Update(long id, UpdateAlertRequest request, CancellationToken ct)
    {
        var result = await alerts.UpdateAsync(id, CallerId, request, ct);
        if (ErrorFor(result) is { } problem)
        {
            return problem;
        }

        await AuditAsync(AuditActions.AlertUpdate, AuditTargets.Alert, id);
        return result.Value!;
    }

    [HttpPost("{id:long}/enable")]
    public Task<ActionResult<AlertDto>> Enable(long id, CancellationToken ct) => SetEnabled(id, true, ct);

    [HttpPost("{id:long}/disable")]
    public Task<ActionResult<AlertDto>> Disable(long id, CancellationToken ct) => SetEnabled(id, false, ct);

    [HttpDelete("{id:long}")]
    public async Task<IActionResult> Delete(long id, CancellationToken ct)
    {
        if (!await alerts.DeleteAsync(id, CallerId, ct))
        {
            return NotFound();
        }

        await AuditAsync(AuditActions.AlertDelete, AuditTargets.Alert, id);
        return NoContent();
    }

    /// <summary>Up to 3 matching events of the last 24 h; nothing is sent (spec §2.5, D11).</summary>
    [HttpGet("{id:long}/recent-matches")]
    public async Task<ActionResult<RecentMatchesDto>> RecentMatches(long id, CancellationToken ct) =>
        await alerts.RecentMatchesAsync(id, CallerId, ct) is { } dto ? dto : NotFound();

    private async Task<ActionResult<AlertDto>> SetEnabled(long id, bool enabled, CancellationToken ct)
    {
        var dto = await alerts.SetEnabledAsync(id, CallerId, enabled, ct);
        if (dto is null)
        {
            return NotFound();
        }

        await AuditAsync(enabled ? AuditActions.AlertEnable : AuditActions.AlertDisable, AuditTargets.Alert, id);
        return dto;
    }
}
