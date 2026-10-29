using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sonrisa.Api.Configuration;
using Sonrisa.Application;
using Sonrisa.Application.Services;
using Sonrisa.Contracts.Dtos;
using Sonrisa.Contracts.Dtos.Admin;

namespace Sonrisa.Api.Controllers.Admin;

/// <summary>Read-only user list of the admin view (spec §2.9), paged, ordered by email.</summary>
[ApiController]
[Route("api/admin/users")]
[Authorize(Roles = AdminSeeding.AdminRole)]
public sealed class AdminUsersController(AdminQueryService queries) : ControllerBase
{
    [HttpGet]
    public Task<PagedResult<AdminUserDto>> List([FromQuery] int page = 1,
        [FromQuery] int pageSize = Paging.DefaultPageSize, CancellationToken ct = default) =>
        queries.ListUsersAsync(page, pageSize, ct);
}
