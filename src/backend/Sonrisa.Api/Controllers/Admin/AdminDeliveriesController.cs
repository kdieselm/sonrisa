using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sonrisa.Api.Configuration;
using Sonrisa.Application;
using Sonrisa.Application.Services;
using Sonrisa.Contracts.Dtos;
using Sonrisa.Contracts.Dtos.Admin;

namespace Sonrisa.Api.Controllers.Admin;

/// <summary>Read-only delivery log of the admin view (spec §2.7, §2.9), newest first.</summary>
[ApiController]
[Route("api/admin/deliveries")]
[Authorize(Roles = AdminSeeding.AdminRole)]
public sealed class AdminDeliveriesController(AdminQueryService queries) : ControllerBase
{
    /// <param name="status"><c>Pending</c>, <c>Sent</c> or <c>Failed</c>; empty = all.</param>
    [HttpGet]
    public async Task<ActionResult<PagedResult<AdminDeliveryDto>>> List([FromQuery] string? status,
        [FromQuery] int page = 1, [FromQuery] int pageSize = Paging.DefaultPageSize, CancellationToken ct = default)
    {
        var result = await queries.ListDeliveriesAsync(status, page, pageSize, ct);
        return result is null
            ? ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]>
            {
                ["status"] = ["Use Pending, Sent or Failed."],
            }))
            : result;
    }
}
