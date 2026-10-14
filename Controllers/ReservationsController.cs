using LibraryManagementSystem.Data;
using LibraryManagementSystem.Models;
using LibraryManagementSystem.Services;
using LibraryManagementSystem.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace LibraryManagementSystem.Controllers;

/// <summary>
/// Lets Reception place and cancel holds on borrowed or damaged items.
/// </summary>
[Authorize(Roles = "Reception")]
public class ReservationsController : Controller
{
    private readonly LibraryContext _db;
    private readonly IReservationService _reservations;

    public ReservationsController(LibraryContext db, IReservationService reservations)
    {
        _db = db;
        _reservations = reservations;
    }

    // Lists active holds (Waiting or Ready) with each waiting borrower's queue position.
    public async Task<IActionResult> Index()
    {
        var active = await _db.Reservations
            .Include(r => r.Item).ThenInclude(i => i.Branch)
            .Include(r => r.Borrower)
            .AsNoTracking()
            .Where(r => r.Status == ReservationStatus.Waiting || r.Status == ReservationStatus.Ready)
            .ToListAsync();

        // The desk's own branch, so holds on items held elsewhere can be pointed out.
        ViewBag.DeskBranchId = (await _db.GetStaffBranchAsync(User))?.Id;

        // Ids increase in the order holds are placed, so Id order is queue order.
        var positions = new Dictionary<int, int>();
        foreach (var group in active.Where(r => r.Status == ReservationStatus.Waiting).GroupBy(r => r.ItemId))
        {
            var position = 1;
            foreach (var reservation in group.OrderBy(r => r.Id))
                positions[reservation.Id] = position++;
        }

        ViewBag.QueuePositions = positions;
        return View(active.OrderBy(r => r.Item.Name).ThenBy(r => r.Id).ToList());
    }

    [HttpGet]
    public async Task<IActionResult> Create(string? code = null)
    {
        await LoadBorrowers();
        return View(new ReserveViewModel { LibraryCode = code ?? string.Empty });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(ReserveViewModel model)
    {
        if (!ModelState.IsValid)
        {
            await LoadBorrowers();
            return View(model);
        }

        var item = await _db.FindItemByCodeAsync(model.LibraryCode);
        if (item == null)
            ModelState.AddModelError(nameof(model.LibraryCode), "No item was found with this library code.");
        else if (item.Status == ItemStatus.Available)
            ModelState.AddModelError(nameof(model.LibraryCode), "This item is available now. Borrow it instead of placing a hold.");
        else if (item.Status != ItemStatus.Borrowed && item.Status != ItemStatus.Damaged)
            ModelState.AddModelError(nameof(model.LibraryCode), $"Holds can only be placed on borrowed or damaged items. This item is {item.Status}.");

        var borrower = await _db.Borrowers.FindAsync(model.BorrowerId);
        if (borrower == null)
            ModelState.AddModelError(nameof(model.BorrowerId), "Borrower was not found.");

        if (item != null && borrower != null)
        {
            var alreadyHolding = await _db.Reservations.AnyAsync(r =>
                r.ItemId == item.Id && r.BorrowerId == borrower.Id &&
                (r.Status == ReservationStatus.Waiting || r.Status == ReservationStatus.Ready));
            if (alreadyHolding)
                ModelState.AddModelError(nameof(model.BorrowerId), "This borrower already has an active hold on this item.");

            var hasItemOnLoan = await _db.Loans.AnyAsync(l =>
                l.ItemId == item.Id && l.BorrowerId == borrower.Id && l.ReturnedAt == null);
            if (hasItemOnLoan)
                ModelState.AddModelError(nameof(model.BorrowerId), "This borrower currently has this item on loan.");
        }

        if (!ModelState.IsValid)
        {
            await LoadBorrowers();
            return View(model);
        }

        var reservation = new Reservation { ItemId = item!.Id, BorrowerId = borrower!.Id };
        _db.Reservations.Add(reservation);
        await _db.SaveChangesAsync();

        var position = await _db.Reservations.CountAsync(r =>
            r.ItemId == item.Id && r.Status == ReservationStatus.Waiting && r.Id <= reservation.Id);

        TempData["Success"] = $"Hold placed on {item.Name} for {borrower.FullName}. Queue position: {position}.";
        return RedirectToAction(nameof(Index));
    }

    // Waiting and Ready holds can be cancelled. Cancelling a Ready hold releases the
    // item, so the next borrower in the queue is offered it straight away.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(int id)
    {
        var reservation = await _db.Reservations.AsNoTracking().SingleOrDefaultAsync(r => r.Id == id);
        if (reservation == null) return NotFound();

        if (reservation.Status != ReservationStatus.Waiting && reservation.Status != ReservationStatus.Ready)
        {
            TempData["Error"] = "Only holds that are waiting or ready can be cancelled.";
            return RedirectToAction(nameof(Index));
        }

        var wasReady = reservation.Status == ReservationStatus.Ready;
        if (!await _reservations.CancelHoldAsync(id))
        {
            TempData["Error"] = "This hold was changed by someone else just now. Please check the list and try again.";
            return RedirectToAction(nameof(Index));
        }

        TempData["Success"] = wasReady
            ? "Hold cancelled. The item has been offered to the next borrower in the queue, if there is one."
            : "Hold cancelled.";
        return RedirectToAction(nameof(Index));
    }

    // Staff trigger that expires Ready holds not collected within the hold period.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ExpireOldHolds()
    {
        var expired = await _reservations.ExpireUncollectedHoldsAsync();
        TempData["Success"] = expired == 0
            ? $"Hold check finished. No Ready holds were older than {ReservationService.HoldDays} days."
            : $"Hold check finished. {expired} uncollected hold(s) expired.";
        return RedirectToAction(nameof(Index));
    }
    private async Task LoadBorrowers()
    {
        var borrowers = await _db.Borrowers.AsNoTracking().OrderBy(b => b.FullName).ToListAsync();
        ViewBag.Borrowers = new SelectList(borrowers, "Id", "FullName");
    }
}