using LibraryManagementSystem.Data;
using LibraryManagementSystem.Models;
using LibraryManagementSystem.Services;
using LibraryManagementSystem.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace LibraryManagementSystem.Controllers;

[Authorize(Roles = "Reception")]
public class LoansController : Controller
{
    private readonly LibraryContext _db;
    private readonly INotificationService _notifications;

    public LoansController(LibraryContext db, INotificationService notifications)
    {
        _db = db;
        _notifications = notifications;
    }

    private const string NoDeskMessage = "Your account is not assigned to a branch, so you cannot lend or return items. Ask an Admin to assign your branch.";

    // A Reception account must belong to a branch before it can lend or
    // accept returns. Having no branch must not mean "no restrictions".
    private bool IsReceptionWithoutDesk(Branch? desk) => desk == null && User?.IsInRole("Reception") == true;

    public async Task<IActionResult> Index(int? branchId = null)
    {
        var query = _db.Loans
            .Include(l => l.Item)
            .Include(l => l.Borrower)
            .AsQueryable();
        if (branchId.HasValue) query = query.Where(l => l.BranchId == branchId);

        var branches = await _db.Branches.AsNoTracking().OrderBy(b => b.Name).ToListAsync();
        ViewBag.Branches = branches;
        ViewBag.BranchNames = branches.ToDictionary(b => b.Id, b => b.Name);
        ViewBag.BranchId = branchId;
        var desk = await _db.GetStaffBranchAsync(User);
        ViewBag.StaffBranch = desk;
        ViewBag.NoDesk = IsReceptionWithoutDesk(desk);
        return View(await query.OrderByDescending(l => l.BorrowedAt).ToListAsync());
    }

    [HttpGet]
    public async Task<IActionResult> Borrow(string? code = null)
    {
        if (IsReceptionWithoutDesk(await _db.GetStaffBranchAsync(User)))
        {
            TempData["Error"] = NoDeskMessage;
            return RedirectToAction(nameof(Index));
        }
        await LoadBorrowers();
        return View(new BorrowViewModel { LibraryCode = code ?? string.Empty });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Borrow(BorrowViewModel model)
    {
        var staffBranch = await _db.GetStaffBranchAsync(User);
        if (IsReceptionWithoutDesk(staffBranch))
        {
            TempData["Error"] = NoDeskMessage;
            return RedirectToAction(nameof(Index));
        }

        if (!ModelState.IsValid)
        {
            await LoadBorrowers();
            return View(model);
        }

        var item = await _db.Items.SingleOrDefaultAsync(i => i.LibraryCode == model.LibraryCode);
        if (item == null)
            ModelState.AddModelError(nameof(model.LibraryCode), "No item was found with this library code.");
        else if (item.Status != ItemStatus.Available)
            ModelState.AddModelError(nameof(model.LibraryCode), $"This item is currently {item.Status.Label()}.");
        else
        {
            // A reception desk can only lend what is physically at its branch.
            if (staffBranch != null && item.BranchId != staffBranch.Id)
            {
                var heldAt = await _db.Branches.Where(b => b.Id == item.BranchId).Select(b => b.Name).SingleOrDefaultAsync();
                ModelState.AddModelError(nameof(model.LibraryCode),
                    $"This item is held at {heldAt ?? "another branch"}, not {staffBranch.Name}. Request an inter-branch transfer first.");
            }
        }

        var borrowerExists = await _db.Borrowers.AnyAsync(b => b.Id == model.BorrowerId);
        if (!borrowerExists)
            ModelState.AddModelError(nameof(model.BorrowerId), "Borrower was not found.");

        if (!ModelState.IsValid)
        {
            await LoadBorrowers();
            return View(model);
        }

        var now = DateTime.Now;
        var loan = new Loan
        {
            ItemId = item!.Id,
            BorrowerId = model.BorrowerId,
            BranchId = item.BranchId,
            BorrowedAt = now,
            DueDate = now.Date.AddDays(LibraryRules.LoanDays)
        };

        item.Status = ItemStatus.Borrowed;
        _db.Loans.Add(loan);

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            // Another request borrowed this exact item between our read and our
            // write (Item.Status is configured as a concurrency token). Fail
            // safely instead of creating a second active loan for the same item.
            ModelState.AddModelError(nameof(model.LibraryCode), "This item was just borrowed by someone else. Please try again.");
            await LoadBorrowers();
            return View(model);
        }

        // Simulated notification: tell the borrower what they borrowed and when it is due.
        await _notifications.SendAsync(model.BorrowerId, NotificationType.Borrowed,
            $"You borrowed {item!.Name} ({item.LibraryCode}). It is due back on {loan.DueDate:dd MMM yyyy}.");
        return RedirectToAction(nameof(Index));

    }

    [HttpGet]
    public async Task<IActionResult> Return(int id)
    {
        var loan = await _db.Loans
            .Include(l => l.Item)
            .Include(l => l.Borrower)
            .SingleOrDefaultAsync(l => l.Id == id);

        if (loan == null) return NotFound();
        if (loan.ReturnedAt.HasValue) return RedirectToAction(nameof(Index));

        var desk = await _db.GetStaffBranchAsync(User);
        if (IsReceptionWithoutDesk(desk))
        {
            TempData["Error"] = NoDeskMessage;
            return RedirectToAction(nameof(Index));
        }
        await LoadReturnLocationAsync(loan.Item, desk);

        var lateDays = Math.Max(0, (DateTime.Now.Date - loan.DueDate.Date).Days);
        return View(new ReturnViewModel
        {
            LoanId = loan.Id,
            ItemName = loan.Item.Name,
            LibraryCode = loan.Item.LibraryCode,
            BorrowerName = loan.Borrower.FullName,
            DueDate = loan.DueDate,
            EstimatedFine = lateDays * LibraryRules.FinePerLateDay,
            ReturnStatus = ItemStatus.Available
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Return(ReturnViewModel model)
    {
        if (model.ReturnStatus == ItemStatus.Borrowed)
        {
            ModelState.AddModelError(nameof(model.ReturnStatus), "A returned item cannot remain Borrowed.");
        }
        else if (model.ReturnStatus == ItemStatus.InTransit)
        {
            ModelState.AddModelError(nameof(model.ReturnStatus), "In transit is set by the transfer workflow and cannot be chosen here.");
        }

        var loan = await _db.Loans.Include(l => l.Item).SingleOrDefaultAsync(l => l.Id == model.LoanId);
        if (loan == null) return NotFound();
        if (loan.ReturnedAt.HasValue) return RedirectToAction(nameof(Index));

        var desk = await _db.GetStaffBranchAsync(User);
        if (IsReceptionWithoutDesk(desk))
        {
            TempData["Error"] = NoDeskMessage;
            return RedirectToAction(nameof(Index));
        }

        if (!ModelState.IsValid)
        {
            await LoadReturnLocationAsync(loan.Item, desk);
            model.ItemName = loan.Item.Name;
            model.LibraryCode = loan.Item.LibraryCode;
            model.DueDate = loan.DueDate;
            var borrower = await _db.Borrowers.FindAsync(loan.BorrowerId);
            model.BorrowerName = borrower?.FullName ?? string.Empty;
            return View(model);
        }

        var returnedAt = DateTime.Now;
        var lateDays = Math.Max(0, (returnedAt.Date - loan.DueDate.Date).Days);

        loan.ReturnedAt = returnedAt;
        loan.FineAmount = lateDays * LibraryRules.FinePerLateDay;
        loan.Item.Status = model.ReturnStatus;

        // Cross-branch return: the item is now physically at the desk that
        // took it back, so its recorded location follows it, with a movement
        // record so the change is auditable. The loan's issuing branch is
        // history and does not change.
        string? movedMessage = null;
        if (desk != null && loan.Item.BranchId != desk.Id)
        {
            var previous = await _db.Branches.Where(b => b.Id == loan.Item.BranchId).Select(b => b.Name).SingleOrDefaultAsync();
            await ItemMovements.RecordCrossBranchReturnAsync(_db, loan.Item, desk, User?.Identity?.Name ?? "unknown", loan.Id);
            movedMessage = $"{loan.Item.Name} was returned at {desk.Name}. Its location has been updated from {previous ?? "its previous branch"} to {desk.Name}.";
        }

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            // Item.Status is a concurrency token: someone changed this item
            // between loading the loan and saving the return.
            TempData["Error"] = "This item was changed by someone else just now. The return was not saved; please try again.";
            return RedirectToAction(nameof(Index));
        }

        // Simulated notification: only sent when the return created a late fine.
        if (loan.FineAmount > 0)
        {
            await _notifications.SendAsync(loan.BorrowerId, NotificationType.FineIssued,
                $"A late fine of ${loan.FineAmount:0.00} was added for {loan.Item.Name}, which was due on {loan.DueDate:dd MMM yyyy}.");
        }
        if (movedMessage != null) TempData["Success"] = movedMessage;
        return RedirectToAction(nameof(Index));
    }

    private async Task LoadReturnLocationAsync(Item item, Branch? desk)
    {
        ViewBag.StaffBranch = desk;
        ViewBag.ItemBranchName = await _db.Branches.Where(b => b.Id == item.BranchId).Select(b => b.Name).SingleOrDefaultAsync();
        ViewBag.IsCrossBranchReturn = desk != null && desk.Id != item.BranchId;
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkFinePaid(int id)
    {
        var loan = await _db.Loans.FindAsync(id);
        if (loan == null) return NotFound();
        if (loan.FineAmount > 0) loan.FinePaid = true;
        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }

    private async Task LoadBorrowers()
    {
        var borrowers = await _db.Borrowers.AsNoTracking().OrderBy(b => b.FullName).ToListAsync();
        ViewBag.Borrowers = new SelectList(borrowers, "Id", "FullName");
        ViewBag.StaffBranch = await _db.GetStaffBranchAsync(User);
    }
}
