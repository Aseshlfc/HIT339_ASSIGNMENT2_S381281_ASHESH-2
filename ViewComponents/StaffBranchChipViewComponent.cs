using LibraryManagementSystem.Data;
using Microsoft.AspNetCore.Mvc;

namespace LibraryManagementSystem.ViewComponents;

/// <summary>Shows the signed-in staff member's branch in the page header.</summary>
public class StaffBranchChipViewComponent : ViewComponent
{
    private readonly LibraryContext _db;
    public StaffBranchChipViewComponent(LibraryContext db) => _db = db;

    public async Task<IViewComponentResult> InvokeAsync()
        => View(await _db.GetStaffBranchAsync(UserClaimsPrincipal));
}
