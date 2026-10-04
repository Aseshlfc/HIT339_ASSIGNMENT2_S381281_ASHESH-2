using LibraryManagementSystem.Data;
using LibraryManagementSystem.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LibraryManagementSystem.Controllers;

[Authorize(Roles = "Reception")]
public class BorrowersController : Controller
{
    private readonly LibraryContext _db;
    public BorrowersController(LibraryContext db) => _db = db;

    public async Task<IActionResult> Index()
        => View(await _db.Borrowers.AsNoTracking().OrderBy(b => b.FullName).ToListAsync());

    [HttpGet]
    public IActionResult Create() => View("Form", new Borrower());

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(Borrower borrower)
    {
        if (!ModelState.IsValid) return View("Form", borrower);
        _db.Borrowers.Add(borrower);
        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        var borrower = await _db.Borrowers.FindAsync(id);
        return borrower == null ? NotFound() : View("Form", borrower);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Borrower borrower)
    {
        if (!ModelState.IsValid) return View("Form", borrower);
        if (!await _db.Borrowers.AnyAsync(b => b.Id == borrower.Id)) return NotFound();
        _db.Update(borrower);
        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Delete(int id)
    {
        var borrower = await _db.Borrowers.AsNoTracking().SingleOrDefaultAsync(b => b.Id == id);
        return borrower == null ? NotFound() : View(borrower);
    }

    [HttpPost, ActionName("Delete")]
    [ValidateAntiForgeryToken]
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
}
