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
}

public class ReservationService : IReservationService
{
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
}