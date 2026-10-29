using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sonrisa.Api.Configuration;
using Sonrisa.Application;
using Sonrisa.Application.Services;
using Sonrisa.Contracts.Dtos;
using Sonrisa.Contracts.Dtos.Admin;

namespace Sonrisa.Api.Controllers.Admin;

/// <summary>Read-only audit log of the admin view (spec §2.10), newest first.</summary>
[ApiController]
[Route("api/admin/audit")]
[Authorize(Roles = AdminSeeding.AdminRole)]
public sealed class AdminAuditController(AdminQueryService queries) : ControllerBase
{
    [HttpGet]
    public Task<PagedResult<AuditEntryDto>> List([FromQuery] int page = 1,
        [FromQuery] int pageSize = Paging.DefaultPageSize, CancellationToken ct = default) =>
        queries.ListAuditAsync(page, pageSize, ct);
}
