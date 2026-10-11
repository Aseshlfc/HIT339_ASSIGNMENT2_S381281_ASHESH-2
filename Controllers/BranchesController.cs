using LibraryManagementSystem.Data;
using LibraryManagementSystem.Models;
using LibraryManagementSystem.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LibraryManagementSystem.Controllers;

// All staff can see branches and what each one holds; only Admin can change them.
[Authorize(Roles = "Admin,Reception,Manager")]
public class BranchesController : Controller
{
    private readonly LibraryContext _db;
    public BranchesController(LibraryContext db) => _db = db;

    public async Task<IActionResult> Index()
    {
        var branches = await _db.Branches.AsNoTracking().OrderBy(b => b.Name).ToListAsync();
        var items = await _db.Items.AsNoTracking().Select(i => new { i.BranchId, i.Status }).ToListAsync();
        var staff = await _db.Users.AsNoTracking().Where(u => u.BranchId != null).Select(u => u.BranchId).ToListAsync();
        var open = await _db.ItemTransfers.AsNoTracking()
            .Where(t => t.Status == TransferStatus.Requested || t.Status == TransferStatus.InTransit)
            .Select(t => new { t.FromBranchId, t.ToBranchId })
            .ToListAsync();

        var rows = branches.Select(b => new BranchListItemViewModel
        {
            Branch = b,
            ItemCount = items.Count(i => i.BranchId == b.Id),
            AvailableCount = items.Count(i => i.BranchId == b.Id && i.Status == ItemStatus.Available),
            StaffCount = staff.Count(s => s == b.Id),
            OpenTransferCount = open.Count(t => t.FromBranchId == b.Id || t.ToBranchId == b.Id)
        }).ToList();

        ViewBag.StaffBranch = await _db.GetStaffBranchAsync(User);
        return View(rows);
    }

    public async Task<IActionResult> Details(int id, ItemStatus? status = null)
    {
        var branch = await _db.Branches.AsNoTracking().SingleOrDefaultAsync(b => b.Id == id);
        if (branch == null) return NotFound();

        var items = await _db.Items.AsNoTracking().Where(i => i.BranchId == id).OrderBy(i => i.LibraryCode).ToListAsync();

        var staff = await (
            from user in _db.Users.AsNoTracking()
            where user.BranchId == id
            join userRole in _db.UserRoles on user.Id equals userRole.UserId into userRoles
            from userRole in userRoles.DefaultIfEmpty()
            join role in _db.Roles on userRole.RoleId equals role.Id into roles
            from role in roles.DefaultIfEmpty()
            orderby user.UserName
            select new BranchStaffRow { UserName = user.UserName ?? string.Empty, Role = role.Name ?? "(none)" }
        ).ToListAsync();

        var model = new BranchDetailsViewModel
        {
            Branch = branch,
            TotalItems = items.Count,
            StatusFilter = status,
            Items = status == null ? items : items.Where(i => i.Status == status).ToList(),
            Staff = staff,
            OpenTransfers = await _db.ItemTransfers.AsNoTracking()
                .Include(t => t.Item).Include(t => t.FromBranch).Include(t => t.ToBranch)
                .Where(t => (t.FromBranchId == id || t.ToBranchId == id)
                    && (t.Status == TransferStatus.Requested || t.Status == TransferStatus.InTransit))
                .OrderBy(t => t.RequestedAt)
                .ToListAsync()
        };
        return View(model);
    }

    [HttpGet]
    [Authorize(Roles = "Admin")]
    public IActionResult Create() => View("Form", new Branch());

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Create([Bind("Code,Name,Address,Phone")] Branch branch)
    {
        await ValidateAsync(branch);
        if (!ModelState.IsValid) return View("Form", branch);

        Normalise(branch);
        branch.IsActive = true;
        _db.Branches.Add(branch);
        await _db.SaveChangesAsync();

        TempData["Success"] = $"Branch {branch.Name} was created.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Edit(int id)
    {
        var branch = await _db.Branches.AsNoTracking().SingleOrDefaultAsync(b => b.Id == id);
        return branch == null ? NotFound() : View("Form", branch);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Edit([Bind("Id,Code,Name,Address,Phone")] Branch branch)
    {
        var existing = await _db.Branches.SingleOrDefaultAsync(b => b.Id == branch.Id);
        if (existing == null) return NotFound();

        await ValidateAsync(branch);
        if (!ModelState.IsValid) return View("Form", branch);

        Normalise(branch);
        existing.Code = branch.Code;
        existing.Name = branch.Name;
        existing.Address = branch.Address;
        existing.Phone = branch.Phone;
        await _db.SaveChangesAsync();

        TempData["Success"] = $"Branch {existing.Name} was updated.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> ToggleActive(int id)
    {
        var branch = await _db.Branches.SingleOrDefaultAsync(b => b.Id == id);
        if (branch == null) return NotFound();

        if (!branch.IsActive)
        {
            branch.IsActive = true;
            await _db.SaveChangesAsync();
            TempData["Success"] = $"{branch.Name} is active again.";
            return RedirectToAction(nameof(Index));
        }

        var blocker = await DeactivationBlockerAsync(branch);
        if (blocker != null)
        {
            TempData["Error"] = blocker;
            return RedirectToAction(nameof(Index));
        }

        branch.IsActive = false;
        await _db.SaveChangesAsync();
        TempData["Success"] = $"{branch.Name} was deactivated. It keeps its history but can no longer receive items, transfers or staff.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Delete(int id)
    {
        var branch = await _db.Branches.AsNoTracking().SingleOrDefaultAsync(b => b.Id == id);
        return branch == null ? NotFound() : View(branch);
    }

    [HttpPost, ActionName("Delete")]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        var branch = await _db.Branches.SingleOrDefaultAsync(b => b.Id == id);
        if (branch == null) return NotFound();

        var blocker = await DeletionBlockerAsync(branch);
        if (blocker != null)
        {
            TempData["Error"] = blocker;
            return RedirectToAction(nameof(Index));
        }

        _db.Branches.Remove(branch);
        try
        {
            await _db.SaveChangesAsync();
            TempData["Success"] = $"Branch {branch.Name} was deleted.";
        }
        catch (DbUpdateException)
        {
            // Something started referring to the branch after the checks above.
            TempData["Error"] = $"{branch.Name} is now in use and was not deleted. Please reload and try again.";
        }
        return RedirectToAction(nameof(Index));
    }

    private async Task ValidateAsync(Branch branch)
    {
        var code = (branch.Code ?? string.Empty).Trim().ToUpperInvariant();
        if (code.Length > 0 && await _db.Branches.AnyAsync(b => b.Code.ToUpper() == code && b.Id != branch.Id))
            ModelState.AddModelError(nameof(branch.Code), "Branch code must be unique.");

        var name = (branch.Name ?? string.Empty).Trim().ToLower();
        if (name.Length > 0 && await _db.Branches.AnyAsync(b => b.Name.ToLower() == name && b.Id != branch.Id))
            ModelState.AddModelError(nameof(branch.Name), "Another branch already uses this name.");
    }

    private static void Normalise(Branch branch)
    {
        branch.Code = branch.Code.Trim().ToUpperInvariant();
        branch.Name = branch.Name.Trim();
        branch.Address = branch.Address.Trim();
        branch.Phone = string.IsNullOrWhiteSpace(branch.Phone) ? null : branch.Phone.Trim();
    }

    private async Task<string?> DeactivationBlockerAsync(Branch branch)
    {
        if (branch.Id == Branch.DefaultBranchId)
            return $"{branch.Name} is the default branch and cannot be deactivated.";
        if (await _db.Items.AnyAsync(i => i.BranchId == branch.Id))
            return $"{branch.Name} still holds items. Transfer them to another branch before deactivating it.";
        if (await _db.ItemTransfers.AnyAsync(t => (t.FromBranchId == branch.Id || t.ToBranchId == branch.Id)
                && (t.Status == TransferStatus.Requested || t.Status == TransferStatus.InTransit)))
            return $"{branch.Name} has open transfers. Complete or cancel them before deactivating it.";
        if (await _db.Users.AnyAsync(u => u.BranchId == branch.Id))
            return $"{branch.Name} still has staff assigned. Reassign them before deactivating it.";
        return null;
    }

    private async Task<string?> DeletionBlockerAsync(Branch branch)
    {
        if (branch.Id == Branch.DefaultBranchId)
            return $"{branch.Name} is the default branch and cannot be deleted.";
        if (await _db.Items.AnyAsync(i => i.BranchId == branch.Id))
            return $"{branch.Name} still holds items and cannot be deleted. Transfer the items first.";
        if (await _db.Users.AnyAsync(u => u.BranchId == branch.Id))
            return $"{branch.Name} still has staff assigned and cannot be deleted. Reassign them first.";
        if (await _db.ItemTransfers.AnyAsync(t => t.FromBranchId == branch.Id || t.ToBranchId == branch.Id))
            return $"{branch.Name} appears in transfer history and cannot be deleted. Deactivate it instead.";
        if (await _db.Loans.AnyAsync(l => l.BranchId == branch.Id))
            return $"{branch.Name} appears in loan history and cannot be deleted. Deactivate it instead.";
        return null;
    }
}
