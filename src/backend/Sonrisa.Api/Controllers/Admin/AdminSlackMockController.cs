using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sonrisa.Api.Configuration;
using Sonrisa.Application;
using Sonrisa.Application.Services;
using Sonrisa.Contracts.Dtos;
using Sonrisa.Contracts.Dtos.Admin;

namespace Sonrisa.Api.Controllers.Admin;

/// <summary>Read-only list of the messages the local Slack mock receiver accepted (spec §2.6, §2.9).</summary>
[ApiController]
[Route("api/admin/slack-mock")]
[Authorize(Roles = AdminSeeding.AdminRole)]
public sealed class AdminSlackMockController(AdminQueryService queries) : ControllerBase
{
    [HttpGet]
    public Task<PagedResult<SlackMockMessageDto>> List([FromQuery] int page = 1,
        [FromQuery] int pageSize = Paging.DefaultPageSize, CancellationToken ct = default) =>
        queries.ListSlackMockAsync(page, pageSize, ct);
}
