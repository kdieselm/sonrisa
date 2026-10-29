using Microsoft.AspNetCore.Identity;

namespace Sonrisa.Infrastructure.Persistence;

/// <summary>Application user. Domain entities refer to users only by <c>UserId</c> (architecture §3.3).</summary>
public class AppUser : IdentityUser
{
}
