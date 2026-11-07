using System.Security.Claims;
using Emanet.Core;
using Emanet.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Emanet.Api.Common;

/// <summary>Administrators are the accounts whose emails are listed under <c>Admin:Emails</c>.</summary>
internal static class Admins
{
    public static async Task<bool> IsAdminAsync(this ClaimsPrincipal user, EmanetDbContext db, IConfiguration configuration, CancellationToken ct)
    {
        var admins = configuration.GetSection("Admin:Emails").Get<string[]>() ?? [];
        if (admins.Length == 0)
        {
            return false;
        }

        var me = await db.Users.AsNoTracking().Where(u => u.Id == user.UserId()).Select(u => u.Email).SingleOrDefaultAsync(ct);
        return me is not null && admins.Any(a => string.Equals(a.Trim(), me, StringComparison.OrdinalIgnoreCase));
    }

    public static async Task RequireAdminAsync(this ClaimsPrincipal user, EmanetDbContext db, IConfiguration configuration, CancellationToken ct)
    {
        if (!await user.IsAdminAsync(db, configuration, ct))
        {
            throw new DomainException(ErrorCodes.Forbidden, "Only administrators can do this.");
        }
    }
}
