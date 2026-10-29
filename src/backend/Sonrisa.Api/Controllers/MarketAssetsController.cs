using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sonrisa.Domain.Categories.Market;

namespace Sonrisa.Api.Controllers;

/// <summary>The assets a market alert or a simulated market event can name (spec §2.5): a list in configuration.</summary>
[ApiController]
[Route("api/market/assets")]
[Authorize]
public sealed class MarketAssetsController(MarketAssetCatalog assets) : ControllerBase
{
    [HttpGet]
    public IReadOnlyList<MarketAsset> Get() => assets.All;
}
