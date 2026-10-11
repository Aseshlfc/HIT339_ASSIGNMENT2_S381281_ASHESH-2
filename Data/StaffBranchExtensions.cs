using System.Security.Claims;
using LibraryManagementSystem.Models;
using Microsoft.EntityFrameworkCore;

namespace LibraryManagementSystem.Data;

public static class StaffBranchExtensions
{
    /// <summary>
    /// Returns the branch the signed-in staff member is assigned to, or null
    /// when nobody is signed in or the account has no branch.
    /// </summary>
    public static async Task<Branch?> GetStaffBranchAsync(this LibraryContext db, ClaimsPrincipal? user)
    {
        var userName = user?.Identity?.Name;
        if (string.IsNullOrEmpty(userName)) return null;

        var normalized = userName.ToUpperInvariant();
        var branchId = await db.Users
            .Where(u => u.NormalizedUserName == normalized)
            .Select(u => u.BranchId)
            .FirstOrDefaultAsync();

        return branchId == null ? null : await db.Branches.AsNoTracking().SingleOrDefaultAsync(b => b.Id == branchId);
    }
}
