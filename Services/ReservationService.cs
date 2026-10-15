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
}

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

        var overdueHolds = await _db.Reservations
            .Include(r => r.Item)
            .Where(r => r.Status == ReservationStatus.Ready && r.ReadyAt != null && r.ReadyAt < cutoff)
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
}