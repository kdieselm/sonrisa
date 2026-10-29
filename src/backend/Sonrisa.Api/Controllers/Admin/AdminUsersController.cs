using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sonrisa.Api.Configuration;
using Sonrisa.Application;
using Sonrisa.Application.Services;
using Sonrisa.Contracts.Dtos;
using Sonrisa.Contracts.Dtos.Admin;

namespace Sonrisa.Api.Controllers.Admin;

/// <summary>Read-only users of the admin view (spec §2.9): the list (paged, by email) and one user's alerts and destinations.</summary>
[ApiController]
[Route("api/admin/users")]
[Authorize(Roles = AdminSeeding.AdminRole)]
public sealed class AdminUsersController(AdminQueryService queries) : ControllerBase
{
    [HttpGet]
    public Task<PagedResult<AdminUserDto>> List([FromQuery] int page = 1,
        [FromQuery] int pageSize = Paging.DefaultPageSize, CancellationToken ct = default) =>
        queries.ListUsersAsync(page, pageSize, ct);

    /// <summary>One user with their alerts and destinations; webhook URLs masked (AC18).</summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<AdminUserDetailDto>> Get(string id, CancellationToken ct) =>
        await queries.GetUserAsync(id, ct) is { } dto ? dto : NotFound();
}
