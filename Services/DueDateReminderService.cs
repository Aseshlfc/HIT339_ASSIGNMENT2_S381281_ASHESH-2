using LibraryManagementSystem.Data;
using LibraryManagementSystem.Models;
using Microsoft.EntityFrameworkCore;

namespace LibraryManagementSystem.Services;

/// <summary>
/// Sends simulated "due soon" reminders for loans that are about to fall due.
/// </summary>
public interface IDueDateReminderService
{
    Task<int> SendDueSoonRemindersAsync();
}

public class DueDateReminderService : IDueDateReminderService
{
    // Loans due today or within this many days get a reminder.
    public const int ReminderDays = 2;

    private readonly LibraryContext _db;
    private readonly INotificationService _notifications;

    public DueDateReminderService(LibraryContext db, INotificationService notifications)
    {
        _db = db;
        _notifications = notifications;
    }

    // Returns how many new reminders were saved.
    public async Task<int> SendDueSoonRemindersAsync()
    {
        var today = DateTime.Now.Date;
        var windowEnd = today.AddDays(ReminderDays + 1);

        var dueSoon = await _db.Loans
            .Include(l => l.Item)
            .AsNoTracking()
            .Where(l => l.ReturnedAt == null && l.DueDate >= today && l.DueDate < windowEnd)
            .ToListAsync();

        var before = await _db.Notifications.CountAsync();

        foreach (var loan in dueSoon)
        {
            var message = $"Reminder: {loan.Item.Name} ({loan.Item.LibraryCode}) is due back on {loan.DueDate:dd MMM yyyy}.";

            // The message includes the item and due date, so an identical message
            // means this borrower has already been reminded about this loan.
            var alreadySent = await _db.Notifications.AnyAsync(n =>
                n.BorrowerId == loan.BorrowerId && n.Type == NotificationType.DueSoon && n.Message == message);
            if (alreadySent) continue;

            await _notifications.SendAsync(loan.BorrowerId, NotificationType.DueSoon, message);
        }

        // Borrowers with no email or phone are skipped by NotificationService,
        // so count the rows that were actually saved.
        return await _db.Notifications.CountAsync() - before;
    }
}