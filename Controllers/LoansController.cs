using LibraryManagementSystem.Data;
using LibraryManagementSystem.Models;
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
    public LoansController(LibraryContext db) => _db = db;

    public async Task<IActionResult> Index()
    {
        var loans = await _db.Loans
            .Include(l => l.Item)
            .Include(l => l.Borrower)
            .OrderByDescending(l => l.BorrowedAt)
            .ToListAsync();
        return View(loans);
    }

    [HttpGet]
    public async Task<IActionResult> Borrow(string? code = null)
    {
        await LoadBorrowers();
        return View(new BorrowViewModel { LibraryCode = code ?? string.Empty });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Borrow(BorrowViewModel model)
    {
        if (!ModelState.IsValid)
        {
            await LoadBorrowers();
            return View(model);
        }

        var item = await _db.Items.SingleOrDefaultAsync(i => i.LibraryCode == model.LibraryCode);
        if (item == null)
            ModelState.AddModelError(nameof(model.LibraryCode), "No item was found with this library code.");
        else if (item.Status != ItemStatus.Available)
            ModelState.AddModelError(nameof(model.LibraryCode), $"This item is currently {item.Status}.");

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

        var loan = await _db.Loans.Include(l => l.Item).SingleOrDefaultAsync(l => l.Id == model.LoanId);
        if (loan == null) return NotFound();
        if (loan.ReturnedAt.HasValue) return RedirectToAction(nameof(Index));

        if (!ModelState.IsValid)
        {
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

        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
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
    }
}
