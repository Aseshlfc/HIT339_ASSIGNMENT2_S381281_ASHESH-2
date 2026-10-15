using LibraryManagementSystem.Data;
using LibraryManagementSystem.Models;

namespace LibraryManagementSystem.Services;

/// <summary>
/// Single place where all simulated notifications are created.
/// </summary>
public interface INotificationService
{
    Task SendAsync(int borrowerId, NotificationType type, string message);
}

public class NotificationService : INotificationService
{
    private readonly LibraryContext _context;
    private readonly ILogger<NotificationService> _logger;

    public NotificationService(LibraryContext context, ILogger<NotificationService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task SendAsync(int borrowerId, NotificationType type, string message)
    {
        var borrower = await _context.Borrowers.FindAsync(borrowerId);
        if (borrower == null)
        {
            _logger.LogWarning("Notification skipped: borrower {BorrowerId} not found.", borrowerId);
            return;
        }

        // Prefer email; fall back to SMS; skip if the borrower has no contact details.
        NotificationChannel channel;
        if (!string.IsNullOrWhiteSpace(borrower.Email))
            channel = NotificationChannel.Email;
        else if (!string.IsNullOrWhiteSpace(borrower.Phone))
            channel = NotificationChannel.Sms;
        else
        {
            _logger.LogWarning("Notification skipped: {Name} has no email or phone.", borrower.FullName);
            return;
        }

        _context.Notifications.Add(new Notification
        {
            BorrowerId = borrowerId,
            Type = type,
            Channel = channel,
            Message = message
        });
        await _context.SaveChangesAsync();

        // Simulated sending: write to the console instead of a real email/SMS.
        _logger.LogInformation("[{Channel}] to {Name}: {Message}", channel, borrower.FullName, message);
    }
}