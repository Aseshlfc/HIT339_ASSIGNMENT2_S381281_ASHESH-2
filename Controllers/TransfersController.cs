using LibraryManagementSystem.Data;
using LibraryManagementSystem.Models;
using LibraryManagementSystem.Services;
using LibraryManagementSystem.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LibraryManagementSystem.Controllers;

// Admin can act for any branch. Reception staff act for the branch their
// account is assigned to: the source desk dispatches or rejects, the
// destination desk receives, and either side can request or cancel.
[Authorize(Roles = "Admin,Reception")]
public class TransfersController : Controller
{
    private const string NoBranchMessage = "Your account is not assigned to a branch, so you cannot manage transfers. Ask an Admin to assign your branch.";

    private readonly LibraryContext _db;
    private readonly ITransferService _transfers;

    public TransfersController(LibraryContext db, ITransferService transfers)
    {
        _db = db;
        _transfers = transfers;
    }

    public async Task<IActionResult> Index(string view = "open", int? branchId = null)
    {
        var staffBranch = await _db.GetStaffBranchAsync(User);
        var isAdmin = User.IsInRole("Admin");

        var query = _db.ItemTransfers.AsNoTracking()
            .Include(t => t.Item).Include(t => t.FromBranch).Include(t => t.ToBranch)
            .AsQueryable();

        query = view == "history"
            ? query.Where(t => t.Status != TransferStatus.Requested && t.Status != TransferStatus.InTransit)
            : view == "all"
                ? query
                : query.Where(t => t.Status == TransferStatus.Requested || t.Status == TransferStatus.InTransit);

        if (branchId.HasValue)
            query = query.Where(t => t.FromBranchId == branchId || t.ToBranchId == branchId);

        var transfers = await query.OrderByDescending(t => t.RequestedAt).ToListAsync();

        ViewBag.Branches = await _db.Branches.AsNoTracking().OrderBy(b => b.Name).ToListAsync();
        return View(new TransferIndexViewModel
        {
            Rows = transfers.Select(t => ToRow(t, isAdmin, staffBranch)).ToList(),
            View = view is "history" or "all" ? view : "open",
            BranchId = branchId,
            StaffBranch = staffBranch,
            IsAdmin = isAdmin,
            CanCreate = isAdmin || staffBranch != null
        });
    }

    public async Task<IActionResult> Details(int id)
    {
        var transfer = await _db.ItemTransfers.AsNoTracking()
            .Include(t => t.Item).Include(t => t.FromBranch).Include(t => t.ToBranch)
            .SingleOrDefaultAsync(t => t.Id == id);
        if (transfer == null) return NotFound();

        var staffBranch = await _db.GetStaffBranchAsync(User);
        return View(ToRow(transfer, User.IsInRole("Admin"), staffBranch));
    }

    [HttpGet]
    public async Task<IActionResult> Create(int? itemId = null)
    {
        var staffBranch = await _db.GetStaffBranchAsync(User);
        if (!User.IsInRole("Admin") && staffBranch == null)
        {
            TempData["Error"] = NoBranchMessage;
            return RedirectToAction(nameof(Index));
        }

        await LoadCreateLookupsAsync(staffBranch);
        return View(new TransferCreateViewModel { ItemId = itemId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(TransferCreateViewModel model)
    {
        var staffBranch = await _db.GetStaffBranchAsync(User);
        var isAdmin = User.IsInRole("Admin");
        if (!isAdmin && staffBranch == null)
        {
            TempData["Error"] = NoBranchMessage;
            return RedirectToAction(nameof(Index));
        }

        if (ModelState.IsValid && !isAdmin)
        {
            // Reception can send an item from their own desk or request one to it,
            // but cannot move stock between two other branches.
            var itemBranchId = await _db.Items.Where(i => i.Id == model.ItemId)
                .Select(i => (int?)i.BranchId).SingleOrDefaultAsync();
            if (itemBranchId != null && itemBranchId != staffBranch!.Id && model.ToBranchId != staffBranch.Id)
                ModelState.AddModelError(string.Empty, $"You can only transfer items from or to your own branch ({staffBranch.Name}).");
        }

        if (ModelState.IsValid)
        {
            var result = await _transfers.RequestAsync(model.ItemId!.Value, model.ToBranchId!.Value, model.Reason, ActorName);
            if (result.Succeeded)
            {
                TempData["Success"] = result.Message;
                return RedirectToAction(nameof(Details), new { id = result.Transfer!.Id });
            }
            ModelState.AddModelError(string.Empty, result.Message);
        }

        await LoadCreateLookupsAsync(staffBranch);
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> Dispatch(int id)
        => ActAsync(id, row => row.CanDispatch, "Only the source branch can dispatch this transfer.",
            () => _transfers.DispatchAsync(id, ActorName));

    [HttpPost]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> Receive(int id)
        => ActAsync(id, row => row.CanReceive, "Only the destination branch can receive this transfer.",
            () => _transfers.ReceiveAsync(id, ActorName));

    [HttpPost]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> Reject(int id, string? note = null)
        => ActAsync(id, row => row.CanReject, "Only the source branch can reject this transfer.",
            () => _transfers.RejectAsync(id, ActorName, note));

    [HttpPost]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> Cancel(int id, string? note = null)
        => ActAsync(id, row => row.CanCancel, "Only the source or destination branch can cancel this transfer.",
            () => _transfers.CancelAsync(id, ActorName, note));

    private string ActorName => User.Identity?.Name ?? "unknown";

    private async Task<IActionResult> ActAsync(int id, Func<TransferRowViewModel, bool> allowed, string deniedMessage, Func<Task<TransferResult>> action)
    {
        var transfer = await _db.ItemTransfers.AsNoTracking().SingleOrDefaultAsync(t => t.Id == id);
        if (transfer == null) return NotFound();

        var staffBranch = await _db.GetStaffBranchAsync(User);
        var isAdmin = User.IsInRole("Admin");

        // Permission is checked on who may act for the branch, ignoring the
        // current status: the service reports status problems (for example a
        // transfer that was already completed) with a specific message.
        var permissions = ToRow(transfer, isAdmin, staffBranch, ignoreStatus: true);
        if (!allowed(permissions))
        {
            TempData["Error"] = !isAdmin && staffBranch == null ? NoBranchMessage : deniedMessage;
            return RedirectToAction(nameof(Details), new { id });
        }

        var result = await action();
        TempData[result.Succeeded ? "Success" : "Error"] = result.Message;
        return RedirectToAction(nameof(Details), new { id });
    }

    private static TransferRowViewModel ToRow(ItemTransfer transfer, bool isAdmin, Branch? staffBranch, bool ignoreStatus = false)
    {
        bool ActsFor(int branchId) => isAdmin || staffBranch?.Id == branchId;
        var requested = ignoreStatus || transfer.Status == TransferStatus.Requested;
        var inTransit = ignoreStatus || transfer.Status == TransferStatus.InTransit;

        return new TransferRowViewModel
        {
            Transfer = transfer,
            CanDispatch = requested && ActsFor(transfer.FromBranchId),
            CanReject = requested && ActsFor(transfer.FromBranchId),
            CanReceive = inTransit && ActsFor(transfer.ToBranchId),
            CanCancel = (requested || inTransit) && (ActsFor(transfer.FromBranchId) || ActsFor(transfer.ToBranchId))
        };
    }

    private async Task LoadCreateLookupsAsync(Branch? staffBranch)
    {
        var branches = await _db.Branches.AsNoTracking().OrderBy(b => b.Name).ToListAsync();
        var openItemIds = await _db.ItemTransfers.AsNoTracking()
            .Where(t => t.Status == TransferStatus.Requested || t.Status == TransferStatus.InTransit)
            .Select(t => t.ItemId)
            .ToListAsync();

        // Only items that can actually be transferred are offered.
        var items = (await _db.Items.AsNoTracking()
                .Where(i => i.Status == ItemStatus.Available)
                .OrderBy(i => i.LibraryCode)
                .ToListAsync())
            .Where(i => !openItemIds.Contains(i.Id))
            .ToList();

        ViewBag.Items = items;
        ViewBag.BranchNames = branches.ToDictionary(b => b.Id, b => b.Name);
        ViewBag.Destinations = branches.Where(b => b.IsActive).ToList();
        ViewBag.StaffBranch = staffBranch;
    }
}
