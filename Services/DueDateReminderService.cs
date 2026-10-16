using LibraryManagementSystem.Data;
using LibraryManagementSystem.Models;
using Microsoft.EntityFrameworkCore;

namespace LibraryManagementSystem.Services;

/// <summary>
/// Sends simulated "due soon" reminders for loans that are about to fall due,
/// and "fine accruing" notices for loans that are overdue and not yet returned.
/// </summary>
public interface IDueDateReminderService
{
    Task<int> SendDueSoonRemindersAsync();
    Task<int> SendOverdueFineNoticesAsync();
}

public class DueDateReminderService : IDueDateReminderService
{
    // Loans due today or within this many days get a reminder.
    public const int ReminderDays = 2;

    // An overdue loan gets a fine notice the first time the check runs after its
    // due date, then at most once every this many days until it is returned.
    public const int OverdueNoticeIntervalDays = 7;

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

    // Returns how many new overdue notices were saved. The fine itself is still
    // charged by LoansController when the item comes back; this only warns the
    // borrower how much has built up so far.
    public async Task<int> SendOverdueFineNoticesAsync()
    {
        var today = DateTime.Now.Date;

        // Same rule as the reports and the kiosk: overdue once the due date has passed.
        var overdue = await _db.Loans
            .Include(l => l.Item)
            .AsNoTracking()
            .Where(l => l.ReturnedAt == null && l.DueDate < today)
            .ToListAsync();

        var before = await _db.Notifications.CountAsync();

        foreach (var loan in overdue)
        {
            // Every notice for a loan starts with its loan number, so earlier
            // notices can be found even though the amount in the message changes.
            var prefix = OverdueNoticePrefix(loan.Id);
            var lastSent = await _db.Notifications
                .Where(n => n.BorrowerId == loan.BorrowerId && n.Type == NotificationType.FineAccruing && n.Message.StartsWith(prefix))
                .OrderByDescending(n => n.CreatedAt)
                .Select(n => (DateTime?)n.CreatedAt)
                .FirstOrDefaultAsync();

            // CreatedAt is stored in UTC; compare calendar days in local time.
            if (lastSent.HasValue && lastSent.Value.ToLocalTime().Date > today.AddDays(-OverdueNoticeIntervalDays)) continue;

            var daysOverdue = (today - loan.DueDate.Date).Days;
            var fineSoFar = daysOverdue * LibraryRules.FinePerLateDay;
            var message = $"{prefix} {loan.Item.Name} ({loan.Item.LibraryCode}) was due back on {loan.DueDate:dd MMM yyyy} and is {daysOverdue} day(s) overdue. " +
                          $"A late fine of ${fineSoFar:0.00} has built up so far at ${LibraryRules.FinePerLateDay:0.00} per day, and it will be charged when the item is returned.";

            await _notifications.SendAsync(loan.BorrowerId, NotificationType.FineAccruing, message);
        }

        return await _db.Notifications.CountAsync() - before;
    }

    public static string OverdueNoticePrefix(int loanId) => $"Overdue notice for loan #{loanId}:";
}