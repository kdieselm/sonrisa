using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sonrisa.Application.Services;
using Sonrisa.Contracts.Dtos.Catalog;

namespace Sonrisa.Api.Controllers;

/// <summary>The registered categories and channels the alert and destination forms offer (spec §2.11).</summary>
[ApiController]
[Route("api/catalog")]
[Authorize]
public sealed class CatalogController(CatalogService catalog) : ControllerBase
{
    [HttpGet]
    public Task<CatalogDto> Get(CancellationToken ct) =>
        catalog.GetAsync(User.FindFirstValue(ClaimTypes.NameIdentifier)!, ct);
}
