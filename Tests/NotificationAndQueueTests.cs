using LibraryManagementSystem.Data;
using LibraryManagementSystem.Models;
using LibraryManagementSystem.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LibraryManagementSystem.Tests;

public class NotificationServiceTests
{
    private static NotificationService CreateService(LibraryContext db) =>
        new NotificationService(db, NullLogger<NotificationService>.Instance);

    private static async Task<Borrower> AddBorrowerAsync(LibraryContext db, string email, string phone)
    {
        var borrower = new Borrower { FullName = "Test Borrower", Email = email, Phone = phone, Address = "1 Test St" };
        db.Borrowers.Add(borrower);
        await db.SaveChangesAsync();
        return borrower;
    }

    [Fact]
    public async Task BorrowerWithEmail_ReceivesAnEmailNotification()
    {
        using var db = TestDb.Create();
        var borrower = await AddBorrowerAsync(db, "test@example.com", "0400000000");

        await CreateService(db).SendAsync(borrower.Id, NotificationType.Borrowed, "You borrowed a book.");

        var saved = await db.Notifications.SingleAsync();
        Assert.Equal(NotificationChannel.Email, saved.Channel);
        Assert.Equal(NotificationType.Borrowed, saved.Type);
        Assert.Equal("You borrowed a book.", saved.Message);
    }

    [Fact]
    public async Task BorrowerWithOnlyAPhone_ReceivesAnSmsNotification()
    {
        using var db = TestDb.Create();
        var borrower = await AddBorrowerAsync(db, "", "0400000000");

        await CreateService(db).SendAsync(borrower.Id, NotificationType.FineIssued, "A fine was added.");

        Assert.Equal(NotificationChannel.Sms, (await db.Notifications.SingleAsync()).Channel);
    }

    [Fact]
    public async Task BorrowerWithNoContactDetails_IsSkipped()
    {
        using var db = TestDb.Create();
        var borrower = await AddBorrowerAsync(db, "", "");

        await CreateService(db).SendAsync(borrower.Id, NotificationType.Borrowed, "Hello.");

        Assert.Equal(0, await db.Notifications.CountAsync());
    }

    [Fact]
    public async Task UnknownBorrower_IsSkippedWithoutError()
    {
        using var db = TestDb.Create();

        await CreateService(db).SendAsync(9999, NotificationType.Borrowed, "Hello.");

        Assert.Equal(0, await db.Notifications.CountAsync());
    }
}

public class ReservationQueueTests
{
    private static ReservationService CreateService(LibraryContext db)
    {
        var notifications = new NotificationService(db, NullLogger<NotificationService>.Instance);
        return new ReservationService(db, notifications);
    }

    private static async Task<(Item item, Borrower first, Borrower second)> SeedAsync(LibraryContext db, ItemStatus status)
    {
        var first = new Borrower { FullName = "First Borrower", Email = "first@example.com", Phone = "0400000001", Address = "1 Test St" };
        var second = new Borrower { FullName = "Second Borrower", Email = "second@example.com", Phone = "0400000002", Address = "2 Test St" };
        var item = new Book { Name = "Test Book", Description = "A test item.", LibraryCode = "Q100", Status = status, Author = "Author", Genre = "Genre" };
        db.Borrowers.AddRange(first, second);
        db.Items.Add(item);
        await db.SaveChangesAsync();
        return (item, first, second);
    }

    [Fact]
    public async Task AvailableItem_MakesOnlyTheFirstWaitingHoldReady()
    {
        using var db = TestDb.Create();
        var (item, first, second) = await SeedAsync(db, ItemStatus.Available);
        var firstHold = new Reservation { ItemId = item.Id, BorrowerId = first.Id };
        var secondHold = new Reservation { ItemId = item.Id, BorrowerId = second.Id };
        db.Reservations.Add(firstHold);
        await db.SaveChangesAsync();
        db.Reservations.Add(secondHold);
        await db.SaveChangesAsync();

        await CreateService(db).NotifyNextInQueueAsync(item);

        Assert.Equal(ReservationStatus.Ready, (await db.Reservations.FindAsync(firstHold.Id))!.Status);
        Assert.NotNull((await db.Reservations.FindAsync(firstHold.Id))!.ReadyAt);
        Assert.Equal(ReservationStatus.Waiting, (await db.Reservations.FindAsync(secondHold.Id))!.Status);
        var notification = await db.Notifications.SingleAsync();
        Assert.Equal(first.Id, notification.BorrowerId);
        Assert.Equal(NotificationType.ReservationReady, notification.Type);
    }

    [Fact]
    public async Task ItemThatIsStillBorrowed_DoesNotNotifyAnyone()
    {
        using var db = TestDb.Create();
        var (item, first, _) = await SeedAsync(db, ItemStatus.Borrowed);
        db.Reservations.Add(new Reservation { ItemId = item.Id, BorrowerId = first.Id });
        await db.SaveChangesAsync();

        await CreateService(db).NotifyNextInQueueAsync(item);

        Assert.Equal(ReservationStatus.Waiting, (await db.Reservations.SingleAsync()).Status);
        Assert.Equal(0, await db.Notifications.CountAsync());
    }

    [Fact]
    public async Task ItemAlreadyHeldAsReady_DoesNotMakeASecondHoldReady()
    {
        using var db = TestDb.Create();
        var (item, first, second) = await SeedAsync(db, ItemStatus.Available);
        db.Reservations.Add(new Reservation { ItemId = item.Id, BorrowerId = first.Id, Status = ReservationStatus.Ready, ReadyAt = DateTime.UtcNow });
        var waiting = new Reservation { ItemId = item.Id, BorrowerId = second.Id };
        db.Reservations.Add(waiting);
        await db.SaveChangesAsync();

        await CreateService(db).NotifyNextInQueueAsync(item);

        Assert.Equal(ReservationStatus.Waiting, (await db.Reservations.FindAsync(waiting.Id))!.Status);
        Assert.Equal(0, await db.Notifications.CountAsync());
    }

    [Fact]
    public async Task ItemWithNoQueue_DoesNothing()
    {
        using var db = TestDb.Create();
        var (item, _, _) = await SeedAsync(db, ItemStatus.Available);

        await CreateService(db).NotifyNextInQueueAsync(item);

        Assert.Equal(0, await db.Notifications.CountAsync());
    }
}
