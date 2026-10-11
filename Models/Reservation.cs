namespace LibraryManagementSystem.Models;

/// <summary>
/// A hold placed by a borrower on an item that is currently unavailable.
/// Queue position is the order of CreatedAt among Waiting reservations for the same item.
/// </summary>
public class Reservation
{
    public int Id { get; set; }

    public int ItemId { get; set; }
    public Item Item { get; set; } = null!;

    public int BorrowerId { get; set; }
    public Borrower Borrower { get; set; } = null!;

    public ReservationStatus Status { get; set; } = ReservationStatus.Waiting;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Set when the item becomes available and this borrower is notified.
    // Used later to expire holds that are not collected in time.
    public DateTime? ReadyAt { get; set; }
}

public enum ReservationStatus { Waiting, Ready, Fulfilled, Cancelled, Expired }