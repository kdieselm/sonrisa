using Microsoft.AspNetCore.Mvc;
using Sonrisa.Application;
using Sonrisa.Application.Services;
using Sonrisa.Contracts.Dtos;
using Sonrisa.Contracts.Dtos.Destinations;
using Sonrisa.Domain.Entities;

namespace Sonrisa.Api.Controllers;

/// <summary>The caller's destinations (spec §2.6). Configs are always masked in responses (AC18).</summary>
[Route("api/destinations")]
public sealed class DestinationsController(DestinationService destinations, AuditService audit,
    ILogger<DestinationsController> logger) : UserControllerBase(audit, logger)
{
    [HttpGet]
    public Task<PagedResult<DestinationDto>> List([FromQuery] int page = 1,
        [FromQuery] int pageSize = Paging.DefaultPageSize, CancellationToken ct = default) =>
        destinations.ListAsync(CallerId, page, pageSize, ct);

    [HttpGet("{id:long}")]
    public async Task<ActionResult<DestinationDto>> Get(long id, CancellationToken ct) =>
        await destinations.GetAsync(id, CallerId, ct) is { } dto ? dto : NotFound();

    [HttpPost]
    public async Task<ActionResult<DestinationDto>> Create(CreateDestinationRequest request, CancellationToken ct)
    {
        var result = await destinations.CreateAsync(CallerId, request, ct);
        if (ErrorFor(result) is { } problem)
        {
            return problem;
        }

        await AuditAsync(AuditActions.DestinationCreate, AuditTargets.Destination, result.Value!.Id);
        return StatusCode(StatusCodes.Status201Created, result.Value);
    }

    [HttpPut("{id:long}")]
    public async Task<ActionResult<DestinationDto>> Update(long id, UpdateDestinationRequest request,
        CancellationToken ct)
    {
        var result = await destinations.UpdateAsync(id, CallerId, request, ct);
        if (ErrorFor(result) is { } problem)
        {
            return problem;
        }

        await AuditAsync(AuditActions.DestinationUpdate, AuditTargets.Destination, id);
        return result.Value!;
    }

    [HttpDelete("{id:long}")]
    public async Task<IActionResult> Delete(long id, CancellationToken ct)
    {
        if (!await destinations.DeleteAsync(id, CallerId, ct))
        {
            return NotFound();
        }

        await AuditAsync(AuditActions.DestinationDelete, AuditTargets.Destination, id);
        return NoContent();
    }
}
