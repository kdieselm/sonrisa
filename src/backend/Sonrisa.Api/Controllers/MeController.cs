using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Sonrisa.Contracts.Dtos.Account;
using Sonrisa.Infrastructure.Persistence;

namespace Sonrisa.Api.Controllers;

/// <summary>
/// The caller's email and roles, read from the user store. The SPA uses it to show the admin area; the admin
/// API itself checks the role claim. Lives outside <c>/api/auth</c>, where only the four auth endpoints are open (D22).
/// </summary>
[ApiController]
[Route("api/me")]
[Authorize]
public sealed class MeController(UserManager<AppUser> userManager) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<MeDto>> Get()
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
        {
            return Unauthorized();
        }

        var roles = await userManager.GetRolesAsync(user);
        return new MeDto(user.Email ?? string.Empty, roles.Order().ToList());
    }
}
