using LibraryManagementSystem.Data;
using LibraryManagementSystem.Models;
using LibraryManagementSystem.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LibraryManagementSystem.Controllers;

[Authorize(Roles = "Admin")]
public class StaffController : Controller
{
    private readonly UserManager<ApplicationUser> _userManager;

    // Used only for the branch list. Optional so the controller can still be
    // constructed with just a UserManager.
    private readonly LibraryContext? _db;

    public StaffController(UserManager<ApplicationUser> userManager, LibraryContext? db = null)
    {
        _userManager = userManager;
        _db = db;
    }

    public async Task<IActionResult> Index()
    {
        var users = await _userManager.Users.OrderBy(u => u.UserName).ToListAsync();
        var branchNames = await BranchNamesAsync();
        var rows = new List<StaffListItemViewModel>();
        foreach (var user in users)
        {
            var roles = await _userManager.GetRolesAsync(user);
            rows.Add(new StaffListItemViewModel
            {
                Id = user.Id,
                UserName = user.UserName ?? string.Empty,
                Role = roles.FirstOrDefault() ?? "(none)",
                BranchName = user.BranchId.HasValue && branchNames.TryGetValue(user.BranchId.Value, out var branchName) ? branchName : null,
                IsLockedOut = await _userManager.IsLockedOutAsync(user)
            });
        }
        return View(rows);
    }

    [HttpGet]
    public Task<IActionResult> Create() => FormView(new StaffFormViewModel());

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(StaffFormViewModel model)
    {
        if (string.IsNullOrWhiteSpace(model.Password))
            ModelState.AddModelError(nameof(model.Password), "Password is required when creating an account.");
        if (!StaffRoles.All.Contains(model.Role))
            ModelState.AddModelError(nameof(model.Role), "Choose Admin, Reception or Manager.");
        await ValidateBranchAsync(model, currentBranchId: null);

        if (!ModelState.IsValid) return await FormView(model);

        var user = new ApplicationUser { UserName = model.UserName, BranchId = model.BranchId };
        var result = await _userManager.CreateAsync(user, model.Password!);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors) ModelState.AddModelError(string.Empty, error.Description);
            return await FormView(model);
        }

        await _userManager.AddToRoleAsync(user, model.Role);
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Edit(string id)
    {
        var user = await _userManager.FindByIdAsync(id);
        if (user == null) return NotFound();
        var roles = await _userManager.GetRolesAsync(user);
        return await FormView(new StaffFormViewModel
        {
            Id = user.Id,
            UserName = user.UserName ?? string.Empty,
            Role = roles.FirstOrDefault() ?? "Reception",
            BranchId = user.BranchId
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(StaffFormViewModel model)
    {
        if (string.IsNullOrWhiteSpace(model.Id)) return BadRequest();
        if (!StaffRoles.All.Contains(model.Role))
            ModelState.AddModelError(nameof(model.Role), "Choose Admin, Reception or Manager.");

        var user = await _userManager.FindByIdAsync(model.Id);
        if (user == null) return NotFound();

        var currentRoles = await _userManager.GetRolesAsync(user);
        var currentRole = currentRoles.FirstOrDefault();

        if (currentRole == "Admin" && model.Role != "Admin" && await IsLastAdminAsync(user))
            ModelState.AddModelError(nameof(model.Role), "Cannot change the role of the last remaining Admin account.");
        await ValidateBranchAsync(model, currentBranchId: user.BranchId);

        if (!ModelState.IsValid) return await FormView(model);

        if (!string.Equals(user.UserName, model.UserName, StringComparison.Ordinal))
        {
            var setNameResult = await _userManager.SetUserNameAsync(user, model.UserName);
            if (!setNameResult.Succeeded)
            {
                foreach (var error in setNameResult.Errors) ModelState.AddModelError(string.Empty, error.Description);
                return await FormView(model);
            }
        }

        if (!string.IsNullOrWhiteSpace(model.Password))
        {
            var token = await _userManager.GeneratePasswordResetTokenAsync(user);
            var resetResult = await _userManager.ResetPasswordAsync(user, token, model.Password);
            if (!resetResult.Succeeded)
            {
                foreach (var error in resetResult.Errors) ModelState.AddModelError(string.Empty, error.Description);
                return await FormView(model);
            }
        }

        if (user.BranchId != model.BranchId)
        {
            user.BranchId = model.BranchId;
            await _userManager.UpdateAsync(user);
        }

        if (currentRole != model.Role)
        {
            if (currentRole != null) await _userManager.RemoveFromRoleAsync(user, currentRole);
            await _userManager.AddToRoleAsync(user, model.Role);
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleLock(string id)
    {
        var user = await _userManager.FindByIdAsync(id);
        if (user == null) return NotFound();

        if (await _userManager.IsLockedOutAsync(user))
        {
            await _userManager.SetLockoutEndDateAsync(user, null);
            return RedirectToAction(nameof(Index));
        }

        if (await IsLastAdminAsync(user))
        {
            TempData["Error"] = "Cannot disable the last remaining Admin account.";
            return RedirectToAction(nameof(Index));
        }
        if (user.Id == _userManager.GetUserId(User))
        {
            TempData["Error"] = "You cannot disable your own account while signed in.";
            return RedirectToAction(nameof(Index));
        }

        await _userManager.SetLockoutEnabledAsync(user, true);
        await _userManager.SetLockoutEndDateAsync(user, DateTimeOffset.MaxValue);
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Delete(string id)
    {
        var user = await _userManager.FindByIdAsync(id);
        if (user == null) return NotFound();
        var roles = await _userManager.GetRolesAsync(user);
        return View(new StaffListItemViewModel
        {
            Id = user.Id,
            UserName = user.UserName ?? string.Empty,
            Role = roles.FirstOrDefault() ?? "(none)"
        });
    }

    [HttpPost, ActionName("Delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(string id)
    {
        var user = await _userManager.FindByIdAsync(id);
        if (user == null) return NotFound();

        if (await IsLastAdminAsync(user))
        {
            TempData["Error"] = "Cannot delete the last remaining Admin account.";
            return RedirectToAction(nameof(Index));
        }
        if (user.Id == _userManager.GetUserId(User))
        {
            TempData["Error"] = "You cannot delete your own account while signed in.";
            return RedirectToAction(nameof(Index));
        }

        await _userManager.DeleteAsync(user);
        return RedirectToAction(nameof(Index));
    }

    private async Task<IActionResult> FormView(StaffFormViewModel model)
    {
        ViewBag.Branches = _db == null
            ? new List<Branch>()
            : await _db.Branches.AsNoTracking()
                .Where(b => b.IsActive || b.Id == model.BranchId)
                .OrderBy(b => b.Name)
                .ToListAsync();
        return View("Form", model);
    }

    private async Task<Dictionary<int, string>> BranchNamesAsync()
        => _db == null
            ? new Dictionary<int, string>()
            : await _db.Branches.AsNoTracking().ToDictionaryAsync(b => b.Id, b => b.Name);

    private async Task ValidateBranchAsync(StaffFormViewModel model, int? currentBranchId)
    {
        if (_db == null) return;

        // A reception desk is always at a branch.
        if (model.Role == "Reception" && !model.BranchId.HasValue)
            ModelState.AddModelError(nameof(model.BranchId), "Reception staff must be assigned to a branch.");
        if (!model.BranchId.HasValue || model.BranchId == currentBranchId) return;

        var branch = await _db.Branches.AsNoTracking().SingleOrDefaultAsync(b => b.Id == model.BranchId);
        if (branch == null)
            ModelState.AddModelError(nameof(model.BranchId), "The selected branch was not found.");
        else if (!branch.IsActive)
            ModelState.AddModelError(nameof(model.BranchId), $"{branch.Name} is inactive and cannot have staff assigned.");
    }

    private async Task<bool> IsLastAdminAsync(ApplicationUser user)
    {
        if (!await _userManager.IsInRoleAsync(user, "Admin")) return false;
        var admins = await _userManager.GetUsersInRoleAsync("Admin");
        return admins.Count <= 1;
    }
}
