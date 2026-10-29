using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using Sonrisa.Application;
using Sonrisa.Application.Services;
using Sonrisa.Contracts.Dtos;
using Sonrisa.Contracts.Dtos.Notifications;

namespace Sonrisa.Api.Controllers;

/// <summary>
/// The caller's notifications (spec §2.12): their own delivery rows only; another user's ID is 404 (AC21). Read-only,
/// so nothing is audited.
/// </summary>
[ApiController]
[Route("api/notifications")]
[Authorize]
public sealed class NotificationsController(NotificationService notifications) : ControllerBase
{
    private string CallerId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    [HttpGet]
    public Task<PagedResult<NotificationDto>> List([FromQuery] int page = 1,
        [FromQuery] int pageSize = Paging.DefaultPageSize, CancellationToken ct = default) =>
        notifications.ListAsync(CallerId, page, pageSize, ct);

    [HttpGet("{id:long}")]
    public async Task<ActionResult<NotificationDto>> Get(long id, CancellationToken ct) =>
        await notifications.GetAsync(id, CallerId, ct) is { } dto ? dto : NotFound();
}
