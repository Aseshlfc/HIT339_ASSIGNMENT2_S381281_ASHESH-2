using LibraryManagementSystem.Data;
using LibraryManagementSystem.Models;

namespace LibraryManagementSystem.Services;

/// <summary>
/// Single place where all simulated notifications are created.
/// </summary>
public interface INotificationService
{
    Task SendAsync(int borrowerId, NotificationType type, string message,
                   NotificationChannel channel = NotificationChannel.Email);
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

    public async Task SendAsync(int borrowerId, NotificationType type, string message,
                                NotificationChannel channel = NotificationChannel.Email)
    {
        var notification = new Notification
        {
            BorrowerId = borrowerId,
            Type = type,
            Channel = channel,
            Message = message
        };

        _context.Notifications.Add(notification);
        await _context.SaveChangesAsync();

        // Simulated sending: write to the console instead of a real email/SMS.
        _logger.LogInformation("[{Channel}] to borrower {BorrowerId}: {Message}",
                               channel, borrowerId, message);
    }
}