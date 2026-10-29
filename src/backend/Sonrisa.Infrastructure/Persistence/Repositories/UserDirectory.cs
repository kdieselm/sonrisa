using Microsoft.EntityFrameworkCore;
using Sonrisa.Application;
using Sonrisa.Application.Abstractions;

namespace Sonrisa.Infrastructure.Persistence.Repositories;

/// <summary>Reads the Identity tables directly; no writes (user changes go through <c>UserManager</c>).</summary>
public sealed class UserDirectory(AppDbContext db) : IUserDirectory
{
    public async Task<(IReadOnlyList<UserSummary> Items, int Total)> ListAsync(int page, int pageSize,
        CancellationToken ct)
    {
        var total = await db.Users.CountAsync(ct);
        var users = await db.Users.AsNoTracking()
            .OrderBy(u => u.Email)
            .ThenBy(u => u.Id)
            .Skip(Paging.Offset(page, pageSize))
            .Take(pageSize)
            .Select(u => new { u.Id, u.Email })
            .ToListAsync(ct);

        // Roles only for the users of this page.
        var ids = users.Select(u => u.Id).ToList();
        var roles = await (
                from ur in db.UserRoles
                join r in db.Roles on ur.RoleId equals r.Id
                where ids.Contains(ur.UserId) && r.Name != null
                select new { ur.UserId, Name = r.Name! })
            .AsNoTracking()
            .ToListAsync(ct);

        var rolesByUser = roles
            .GroupBy(r => r.UserId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<string>)g.Select(r => r.Name).Order().ToList());

        var items = users
            .Select(u => new UserSummary(u.Id, u.Email ?? string.Empty,
                rolesByUser.TryGetValue(u.Id, out var r) ? r : []))
            .ToList();
        return (items, total);
    }

    public async Task<IReadOnlyDictionary<string, string>> GetEmailsAsync(IReadOnlyCollection<string> userIds,
        CancellationToken ct)
    {
        if (userIds.Count > Paging.MaxPageSize)
        {
            throw new ArgumentException(
                $"At most {Paging.MaxPageSize} user IDs per call; got {userIds.Count}.", nameof(userIds));
        }

        if (userIds.Count == 0)
        {
            return new Dictionary<string, string>();
        }

        return await db.Users.AsNoTracking()
            .Where(u => userIds.Contains(u.Id) && u.Email != null)
            .ToDictionaryAsync(u => u.Id, u => u.Email!, ct);
    }

    public async Task<UserSummary?> GetAsync(string userId, CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new { u.Id, u.Email })
            .FirstOrDefaultAsync(ct);
        if (user is null)
        {
            return null;
        }

        var roles = await (
                from ur in db.UserRoles
                join r in db.Roles on ur.RoleId equals r.Id
                where ur.UserId == userId && r.Name != null
                select r.Name!)
            .AsNoTracking()
            .OrderBy(n => n)
            .ToListAsync(ct);
        return new UserSummary(user.Id, user.Email ?? string.Empty, roles);
    }

    public async Task<string?> FindIdByEmailAsync(string email, CancellationToken ct)
    {
        // Same normalization as Identity's default (upper-invariant), so the lookup matches its unique user name.
        var normalized = email.ToUpperInvariant();
        return await db.Users.AsNoTracking()
            .Where(u => u.NormalizedEmail == normalized)
            .Select(u => u.Id)
            .FirstOrDefaultAsync(ct);
    }
}
