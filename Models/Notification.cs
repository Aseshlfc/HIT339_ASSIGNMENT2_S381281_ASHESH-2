namespace LibraryManagementSystem.Models;

/// <summary>
/// One simulated email or SMS message sent to a borrower.
/// </summary>
public class Notification
{
    public int Id { get; set; }

    public int BorrowerId { get; set; }
    public Borrower? Borrower { get; set; }

    public NotificationChannel Channel { get; set; }
    public NotificationType Type { get; set; }

    public string Message { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public enum NotificationChannel { Email, Sms }

public enum NotificationType { Borrowed, DueSoon, FineIssued, ReservationReady }