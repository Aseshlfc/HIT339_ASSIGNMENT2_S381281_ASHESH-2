using LibraryManagementSystem.Data;
using LibraryManagementSystem.Models;
using Microsoft.EntityFrameworkCore;

namespace LibraryManagementSystem.Services;

/// <summary>
/// Handles the reservation queue when an item becomes available again.
/// </summary>
public interface IReservationService
{
    Task NotifyNextInQueueAsync(Item item);
    Task<int> ExpireUncollectedHoldsAsync();

    /// <summary>
    /// The one borrowing rule for holds, used by both the reception desk and the kiosk.
    /// An Available item with a queue may only be borrowed by the borrower it is held for.
    /// </summary>
    Task<HoldCheck> CheckBorrowAsync(Item item, int borrowerId);

    /// <summary>True while the item has a Waiting or Ready hold.</summary>
    Task<bool> HasActiveHoldAsync(int itemId);

    /// <summary>Cancels a Waiting or Ready hold. A cancelled Ready hold passes the item to the next borrower.</summary>
    Task<bool> CancelHoldAsync(int reservationId);

    /// <summary>Closes every active hold on an item that has been written off (Destroy) and tells those borrowers.</summary>
    Task<int> CloseHoldsForDestroyedItemAsync(Item item);

    /// <summary>
    /// Puts a Ready hold back to Waiting when its item stops being Available without being
    /// borrowed (for example an Admin marks it Damaged). The borrower keeps first place and
    /// gets a fresh collection period when the item is Available again.
    /// </summary>
    Task<int> ReleaseReadyHoldsAsync(Item item);
}

/// <param name="Allowed">False when the item is being kept for a different borrower.</param>
/// <param name="HoldToFulfil">The borrower's own Ready hold, to be marked Fulfilled with the loan.</param>
public record HoldCheck(bool Allowed, Reservation? HoldToFulfil = null);

public class ReservationService : IReservationService
{
    // A Ready hold that is not collected within this many days expires.
    public const int HoldDays = 3;

    private readonly LibraryContext _db;
    private readonly INotificationService _notifications;

    public ReservationService(LibraryContext db, INotificationService notifications)
    {
        _db = db;
        _notifications = notifications;
    }

    // Marks the first waiting hold as Ready and notifies that borrower.
    // Does nothing if the item is not available, has no queue, or already has a Ready hold.
    public async Task NotifyNextInQueueAsync(Item item)
    {
        if (item.Status != ItemStatus.Available) return;

        var alreadyReady = await _db.Reservations
            .AnyAsync(r => r.ItemId == item.Id && r.Status == ReservationStatus.Ready);
        if (alreadyReady) return;

        // Ids increase in the order holds are placed, so the lowest Id is first in line.
        var next = await _db.Reservations
            .Where(r => r.ItemId == item.Id && r.Status == ReservationStatus.Waiting)
            .OrderBy(r => r.Id)
            .FirstOrDefaultAsync();
        if (next == null) return;

        next.Status = ReservationStatus.Ready;
        next.ReadyAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        await _notifications.SendAsync(next.BorrowerId, NotificationType.ReservationReady,
            $"{item.Name} ({item.LibraryCode}) is now available for you. Please collect it from the library.");
    }

    // Expires Ready holds that were not collected in time, tells the borrower,
    // and offers the item to the next person in the queue. Returns how many expired.
    public async Task<int> ExpireUncollectedHoldsAsync()
    {
        var cutoff = DateTime.UtcNow.AddDays(-HoldDays);

        // The collection period only runs while the item can actually be collected. A Ready
        // hold whose item is not on the shelf is put back to Waiting instead of expiring.
        var notCollectable = await _db.Reservations
            .Include(r => r.Item)
            .Where(r => r.Status == ReservationStatus.Ready &&
                        r.Item.Status != ItemStatus.Available && r.Item.Status != ItemStatus.Destroy)
            .ToListAsync();
        foreach (var item in notCollectable.Select(r => r.Item).Distinct().ToList())
            await ReleaseReadyHoldsAsync(item);

        var overdueHolds = await _db.Reservations
            .Include(r => r.Item)
            .Where(r => r.Status == ReservationStatus.Ready && r.ReadyAt != null && r.ReadyAt < cutoff &&
                        r.Item.Status == ItemStatus.Available)
            .ToListAsync();

        foreach (var hold in overdueHolds)
        {
            hold.Status = ReservationStatus.Expired;
            await _db.SaveChangesAsync();

            await _notifications.SendAsync(hold.BorrowerId, NotificationType.ReservationExpired,
                $"Your hold on {hold.Item.Name} ({hold.Item.LibraryCode}) has expired because it was not collected within {HoldDays} days.");

            // The expired hold no longer counts as Ready, so the next person can be offered the item.
            await NotifyNextInQueueAsync(hold.Item);
        }

        return overdueHolds.Count;
    }

    public async Task<HoldCheck> CheckBorrowAsync(Item item, int borrowerId)
    {
        if (item.Status != ItemStatus.Available) return new HoldCheck(true);

        // An Available item can still have a Waiting queue with nobody Ready (for example
        // straight after a transfer). Offer it to the first in line before anyone borrows it.
        await NotifyNextInQueueAsync(item);

        var readyHold = await _db.Reservations
            .FirstOrDefaultAsync(r => r.ItemId == item.Id && r.Status == ReservationStatus.Ready);
        if (readyHold == null) return new HoldCheck(true);

        return readyHold.BorrowerId == borrowerId
            ? new HoldCheck(true, readyHold)
            : new HoldCheck(false);
    }

    public Task<bool> HasActiveHoldAsync(int itemId) =>
        _db.Reservations.AnyAsync(r => r.ItemId == itemId &&
            (r.Status == ReservationStatus.Waiting || r.Status == ReservationStatus.Ready));

    public async Task<bool> CancelHoldAsync(int reservationId)
    {
        var hold = await _db.Reservations.Include(r => r.Item).SingleOrDefaultAsync(r => r.Id == reservationId);
        if (hold == null) return false;
        if (hold.Status != ReservationStatus.Waiting && hold.Status != ReservationStatus.Ready) return false;

        var wasReady = hold.Status == ReservationStatus.Ready;
        hold.Status = ReservationStatus.Cancelled;
        await _db.SaveChangesAsync();

        // The item was being kept for this borrower, so the next person in line gets it.
        if (wasReady) await NotifyNextInQueueAsync(hold.Item);
        return true;
    }

    public async Task<int> ReleaseReadyHoldsAsync(Item item)
    {
        if (item.Status == ItemStatus.Available || item.Status == ItemStatus.Destroy) return 0;

        var ready = await _db.Reservations
            .Where(r => r.ItemId == item.Id && r.Status == ReservationStatus.Ready)
            .ToListAsync();
        if (ready.Count == 0) return 0;

        foreach (var hold in ready)
        {
            // Back to the front of the queue (queue order is by Id, and this hold is the oldest
            // still open). ReadyAt is cleared so the next offer starts a new collection period.
            hold.Status = ReservationStatus.Waiting;
            hold.ReadyAt = null;
        }
        await _db.SaveChangesAsync();

        foreach (var hold in ready)
        {
            await _notifications.SendAsync(hold.BorrowerId, NotificationType.ReservationDelayed,
                $"{item.Name} ({item.LibraryCode}) is not ready to collect after all. You are still first in the queue and will be told when it is available.");
        }
        return ready.Count;
    }

    public async Task<int> CloseHoldsForDestroyedItemAsync(Item item)
    {
        if (item.Status != ItemStatus.Destroy) return 0;

        var holds = await _db.Reservations
            .Where(r => r.ItemId == item.Id &&
                        (r.Status == ReservationStatus.Waiting || r.Status == ReservationStatus.Ready))
            .OrderBy(r => r.Id)
            .ToListAsync();
        if (holds.Count == 0) return 0;

        foreach (var hold in holds) hold.Status = ReservationStatus.Cancelled;
        await _db.SaveChangesAsync();

        foreach (var hold in holds)
        {
            await _notifications.SendAsync(hold.BorrowerId, NotificationType.ReservationCancelled,
                $"Your hold on {item.Name} ({item.LibraryCode}) was cancelled because the item is no longer available to borrow.");
        }
        return holds.Count;
    }
}