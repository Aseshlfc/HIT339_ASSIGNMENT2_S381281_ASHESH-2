using LibraryManagementSystem.Data;
using LibraryManagementSystem.Models;
using LibraryManagementSystem.Services;
using LibraryManagementSystem.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LibraryManagementSystem.Controllers;

// Reception manages borrowers. Admin can also open the list, but only to set kiosk PINs:
// the add, edit and delete actions stay Reception-only (both attributes must pass).
[Authorize(Roles = "Reception,Admin")]
public class BorrowersController : Controller
{
    private readonly LibraryContext _db;
    private readonly IKioskPinService _pins;

    public BorrowersController(LibraryContext db, IKioskPinService pins)
    {
        _db = db;
        _pins = pins;
    }

    public async Task<IActionResult> Index()
        => View(await _db.Borrowers.AsNoTracking().OrderBy(b => b.FullName).ToListAsync());

    [HttpGet]
    [Authorize(Roles = "Reception")]
    public IActionResult Create() => View("Form", new Borrower());

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = "Reception")]
    public async Task<IActionResult> Create(Borrower borrower)
    {
        if (!ModelState.IsValid) return View("Form", borrower);
        _db.Borrowers.Add(borrower);
        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    [Authorize(Roles = "Reception")]
    public async Task<IActionResult> Edit(int id)
    {
        var borrower = await _db.Borrowers.FindAsync(id);
        return borrower == null ? NotFound() : View("Form", borrower);
    }

    // Only the contact details are copied onto the stored borrower, so saving the form
    // can never clear or change the kiosk PIN fields.
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = "Reception")]
    public async Task<IActionResult> Edit(Borrower borrower)
    {
        if (!ModelState.IsValid) return View("Form", borrower);
        var stored = await _db.Borrowers.SingleOrDefaultAsync(b => b.Id == borrower.Id);
        if (stored == null) return NotFound();

        stored.FullName = borrower.FullName;
        stored.Email = borrower.Email;
        stored.Phone = borrower.Phone;
        stored.Address = borrower.Address;
        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    [Authorize(Roles = "Reception")]
    public async Task<IActionResult> Delete(int id)
    {
        var borrower = await _db.Borrowers.AsNoTracking().SingleOrDefaultAsync(b => b.Id == id);
        return borrower == null ? NotFound() : View(borrower);
    }

    [HttpPost, ActionName("Delete")]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = "Reception")]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        var borrower = await _db.Borrowers.Include(b => b.Loans).SingleOrDefaultAsync(b => b.Id == id);
        if (borrower == null) return NotFound();

        if (borrower.Loans.Any())
        {
            TempData["Error"] = "Borrowers with loan history cannot be deleted. Edit their details instead.";
            return RedirectToAction(nameof(Index));
        }

        _db.Borrowers.Remove(borrower);
        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }

    // Kiosk PIN: Reception and Admin. The PIN is never shown, stored or logged in plain text.
    [HttpGet]
    public async Task<IActionResult> KioskPin(int id)
    {
        var borrower = await _db.Borrowers.AsNoTracking().SingleOrDefaultAsync(b => b.Id == id);
        return borrower == null ? NotFound() : View(new KioskPinViewModel { Borrower = borrower });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> KioskPin(int id, KioskPinForm form)
    {
        var borrower = await _db.Borrowers.AsNoTracking().SingleOrDefaultAsync(b => b.Id == id);
        if (borrower == null) return NotFound();

        if (!KioskPinService.IsValidPin(form.Pin))
            ModelState.AddModelError(nameof(KioskPinForm.Pin), "The PIN must be exactly 6 digits (0-9).");
        else if (form.Pin != form.ConfirmPin)
            ModelState.AddModelError(nameof(KioskPinForm.ConfirmPin), "The two PINs do not match.");

        if (!ModelState.IsValid) return View(new KioskPinViewModel { Borrower = borrower });

        await _pins.SetPinAsync(id, form.Pin!);
        TempData["Success"] = $"Kiosk PIN {(borrower.HasKioskPin ? "reset" : "set")} for {borrower.FullName} ({borrower.CardNumber}). "
            + "Any kiosk session they had open has been ended.";
        return RedirectToAction(nameof(Index));
    }
}
