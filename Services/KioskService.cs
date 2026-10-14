using LibraryManagementSystem.Data;
using LibraryManagementSystem.Models;
using Microsoft.EntityFrameworkCore;

namespace LibraryManagementSystem.Services;

/// <summary>One unreturned loan, as shown on the kiosk account screen.</summary>
public record KioskLoanLine(
    string ItemName,
    string LibraryCode,
    string ItemType,
    DateTime BorrowedAt,
    DateTime DueDate,
    int DaysOverdue,
    decimal EstimatedFine)
{
    public bool IsOverdue => DaysOverdue > 0;
}

/// <summary>A fine from a returned loan that has not been paid yet.</summary>
public record KioskFineLine(string ItemName, string LibraryCode, DateTime ReturnedAt, decimal Amount);

/// <summary>A reservation (Section 3) that is still open for the patron.</summary>
public record KioskHoldLine(
    string ItemName,
    string LibraryCode,
    ReservationStatus Status,
    int QueuePosition,
    DateTime? CollectBy);

public class KioskAccountSummary
{
    public required Borrower Patron { get; init; }
    public required IReadOnlyList<KioskLoanLine> ActiveLoans { get; init; }
    public required IReadOnlyList<KioskFineLine> UnpaidFines { get; init; }
    public required IReadOnlyList<KioskHoldLine> Holds { get; init; }

    public string CardNumber => Patron.CardNumber;
    public decimal UnpaidFineTotal => UnpaidFines.Sum(f => f.Amount);
    public decimal AccruingFineTotal => ActiveLoans.Sum(l => l.EstimatedFine);
}

public record KioskCheckoutResult(bool Succeeded, string Message, Loan? Loan = null, Item? Item = null);

/// <summary>
/// Self-service kiosk rules: finding a patron by card number, summarising their
/// account and lending an item at the kiosk's branch.
/// </summary>
public interface IKioskService
{
    Task<Borrower?> FindPatronAsync(string? cardNumber);
    Task<KioskAccountSummary> GetAccountAsync(Borrower patron);
    Task<KioskCheckoutResult> CheckOutAsync(Branch kioskBranch, Borrower patron, string? libraryCode);
}

public class KioskService : IKioskService
{
    private readonly LibraryContext _db;
    private readonly INotificationService _notifications;
    private readonly IReservationService _reservations;

    public KioskService(LibraryContext db, INotificationService notifications, IReservationService? reservations = null)
    {
        _db = db;
        _notifications = notifications;
        _reservations = reservations ?? new ReservationService(db, notifications);
    }

    public async Task<Borrower?> FindPatronAsync(string? cardNumber)
    {
        if (!Borrower.TryParseCardNumber(cardNumber, out var id)) return null;
        return await _db.Borrowers.AsNoTracking().SingleOrDefaultAsync(b => b.Id == id);
    }

    public async Task<KioskAccountSummary> GetAccountAsync(Borrower patron)
    {
        var today = DateTime.Now.Date;

        var activeLoans = await _db.Loans.AsNoTracking()
            .Include(l => l.Item)
            .Where(l => l.BorrowerId == patron.Id && l.ReturnedAt == null)
            .OrderBy(l => l.DueDate)
            .ToListAsync();

        var loanLines = activeLoans.Select(l =>
        {
            var daysOverdue = Math.Max(0, (today - l.DueDate.Date).Days);
            return new KioskLoanLine(l.Item.Name, l.Item.LibraryCode, l.Item.ItemType,
                l.BorrowedAt, l.DueDate, daysOverdue, daysOverdue * LibraryRules.FinePerLateDay);
        }).ToList();

        // Fine amounts are decimals, which SQLite cannot compare or add up in SQL,
        // so only the non-decimal columns are filtered in the query.
        var returnedLoans = await _db.Loans.AsNoTracking()
            .Include(l => l.Item)
            .Where(l => l.BorrowerId == patron.Id && l.ReturnedAt != null && !l.FinePaid)
            .ToListAsync();
        var fineLines = returnedLoans
            .Where(l => l.FineAmount > 0)
            .OrderBy(l => l.ReturnedAt)
            .Select(l => new KioskFineLine(l.Item.Name, l.Item.LibraryCode, l.ReturnedAt!.Value, l.FineAmount))
            .ToList();

        var reservations = await _db.Reservations.AsNoTracking()
            .Include(r => r.Item)
            .Where(r => r.BorrowerId == patron.Id &&
                        (r.Status == ReservationStatus.Waiting || r.Status == ReservationStatus.Ready))
            .OrderBy(r => r.Id)
            .ToListAsync();

        var holdLines = new List<KioskHoldLine>();
        foreach (var r in reservations)
        {
            var position = 0;
            if (r.Status == ReservationStatus.Waiting)
            {
                // Holds are served in the order they were placed (lowest Id first).
                position = 1 + await _db.Reservations.CountAsync(o =>
                    o.ItemId == r.ItemId && o.Status == ReservationStatus.Waiting && o.Id < r.Id);
            }
            DateTime? collectBy = r.Status == ReservationStatus.Ready && r.ReadyAt.HasValue
                ? r.ReadyAt.Value.AddDays(ReservationService.HoldDays)
                : null;
            holdLines.Add(new KioskHoldLine(r.Item.Name, r.Item.LibraryCode, r.Status, position, collectBy));
        }

        return new KioskAccountSummary
        {
            Patron = patron,
            ActiveLoans = loanLines,
            UnpaidFines = fineLines,
            Holds = holdLines
        };
    }

    // Same rules as LoansController.Borrow, so a kiosk loan and a desk loan are
    // indistinguishable afterwards: only Available items, only items held at this
    // branch, a Ready hold is kept for the patron who reserved it, and the
    // Borrowed notification is sent.
    public async Task<KioskCheckoutResult> CheckOutAsync(Branch kioskBranch, Borrower patron, string? libraryCode)
    {
        var code = libraryCode?.Trim();
        if (string.IsNullOrEmpty(code))
            return new KioskCheckoutResult(false, "Scan or type the library code printed on the item.");

        var item = await _db.FindItemByCodeAsync(code);
        if (item == null)
            return new KioskCheckoutResult(false, $"No item was found with the code \"{code}\". Check the code and try again.");

        if (item.Status != ItemStatus.Available)
            return new KioskCheckoutResult(false,
                $"\"{item.Name}\" is currently {item.Status.Label()} and cannot be borrowed. Ask library staff about placing a hold.");

        if (item.BranchId != kioskBranch.Id)
        {
            var heldAt = await _db.Branches.Where(b => b.Id == item.BranchId).Select(b => b.Name).SingleOrDefaultAsync();
            return new KioskCheckoutResult(false,
                $"\"{item.Name}\" is held at {heldAt ?? "another branch"}, not {kioskBranch.Name}. Ask library staff about a transfer.");
        }

        // Same hold rule as the reception desk: an item with a reservation queue
        // is kept for the first borrower in that queue.
        var holdCheck = await _reservations.CheckBorrowAsync(item, patron.Id);
        if (!holdCheck.Allowed)
            return new KioskCheckoutResult(false, $"\"{item.Name}\" is being held for another patron who reserved it.");
        var readyHold = holdCheck.HoldToFulfil;

        var now = DateTime.Now;
        var loan = new Loan
        {
            ItemId = item.Id,
            BorrowerId = patron.Id,
            BranchId = item.BranchId,
            BorrowedAt = now,
            DueDate = now.Date.AddDays(LibraryRules.LoanDays)
        };

        item.Status = ItemStatus.Borrowed;
        _db.Loans.Add(loan);
        if (readyHold != null) readyHold.Status = ReservationStatus.Fulfilled;

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            // Item.Status is a concurrency token: someone borrowed it between our
            // read and our write. Nothing was saved.
            _db.ChangeTracker.Clear();
            return new KioskCheckoutResult(false, $"\"{item.Name}\" was just borrowed by someone else. Please try again.");
        }

        await _notifications.SendAsync(patron.Id, NotificationType.Borrowed,
            $"You borrowed {item.Name} ({item.LibraryCode}). It is due back on {loan.DueDate:dd MMM yyyy}.");

        return new KioskCheckoutResult(true,
            $"You borrowed \"{item.Name}\". Please return it by {loan.DueDate:dddd d MMMM yyyy}.", loan, item);
    }
}
