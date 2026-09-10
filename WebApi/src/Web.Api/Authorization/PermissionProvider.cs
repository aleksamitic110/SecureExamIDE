using Microsoft.EntityFrameworkCore;
using Web.Api.Database;
using Web.Api.Features.Users;

namespace Web.Api.Authorization;

// Maps a user's role onto the set of permissions that role carries. The mapping lives in code
// rather than in Role/Permission tables: the system has exactly two roles, both of them fixed by
// the domain, so a database-driven mapping would add joins and migrations without adding freedom.
internal sealed class PermissionProvider(ApplicationDbContext context)
{
    public async Task<HashSet<string>> GetForUserIdAsync(Guid userId)
    {
        Account? account = await context.Users
            .AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new Account(u.Role, u.IsActive))
            .SingleOrDefaultAsync();

        // A deleted or deactivated account is granted nothing, which revokes access immediately
        // even while a previously issued access token is still within its lifetime.
        if (account is null || !account.IsActive)
        {
            return [];
        }

        return GetForRole(account.Role);
    }

    public static HashSet<string> GetForRole(Role role) => role switch
    {
        Role.Student =>
        [
            Permissions.UsersRead,
            Permissions.DevicesManage,
            Permissions.ExamsRead,
            Permissions.SubmissionsSubmit
        ],
        Role.Professor =>
        [
            Permissions.UsersRead,
            Permissions.DevicesManage,
            Permissions.ExamsRead,
            Permissions.ExamsManage,
            Permissions.SubmissionsReview
        ],
        _ => []
    };

    private sealed record Account(Role Role, bool IsActive);
}
