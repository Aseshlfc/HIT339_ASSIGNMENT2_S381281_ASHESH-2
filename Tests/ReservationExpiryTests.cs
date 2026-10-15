using LibraryManagementSystem.Data;
using LibraryManagementSystem.Models;
using LibraryManagementSystem.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LibraryManagementSystem.Tests;

public class ReservationExpiryTests
{
    private static ReservationService CreateService(LibraryContext db)
    {
        var notifications = new NotificationService(db, NullLogger<NotificationService>.Instance);
        return new ReservationService(db, notifications);
    }

    // An Available book and two borrowers with contact details.
    private static async Task<(Item item, Borrower first, Borrower second)> SeedAsync(LibraryContext db)
    {
        var first = new Borrower { FullName = "First Borrower", Email = "first@example.com", Phone = "0400000001", Address = "1 Test St" };
        var second = new Borrower { FullName = "Second Borrower", Email = "second@example.com", Phone = "0400000002", Address = "2 Test St" };
        var item = new Book { Name = "Test Book", Description = "A test item.", LibraryCode = "R100", Status = ItemStatus.Available, Author = "Author", Genre = "Genre" };
        db.Borrowers.AddRange(first, second);
        db.Items.Add(item);
        await db.SaveChangesAsync();
        return (item, first, second);
    }

    [Fact]
    public async Task UncollectedHold_Expires_AndTheNextBorrowerIsNotified()
    {
        using var db = TestDb.Create();
        var (item, first, second) = await SeedAsync(db);
        var oldHold = new Reservation { ItemId = item.Id, BorrowerId = first.Id, Status = ReservationStatus.Ready, ReadyAt = DateTime.UtcNow.AddDays(-(ReservationService.HoldDays + 1)) };
        var nextHold = new Reservation { ItemId = item.Id, BorrowerId = second.Id, Status = ReservationStatus.Waiting };
        db.Reservations.AddRange(oldHold, nextHold);
        await db.SaveChangesAsync();

        var expired = await CreateService(db).ExpireUncollectedHoldsAsync();

        Assert.Equal(1, expired);
        Assert.Equal(ReservationStatus.Expired, (await db.Reservations.FindAsync(oldHold.Id))!.Status);
        Assert.Equal(ReservationStatus.Ready, (await db.Reservations.FindAsync(nextHold.Id))!.Status);
        Assert.Contains(await db.Notifications.ToListAsync(), n => n.BorrowerId == first.Id && n.Type == NotificationType.ReservationExpired);
        Assert.Contains(await db.Notifications.ToListAsync(), n => n.BorrowerId == second.Id && n.Type == NotificationType.ReservationReady);
    }

    [Fact]
    public async Task RecentReadyHold_IsNotExpired()
    {
        using var db = TestDb.Create();
        var (item, first, _) = await SeedAsync(db);
        db.Reservations.Add(new Reservation { ItemId = item.Id, BorrowerId = first.Id, Status = ReservationStatus.Ready, ReadyAt = DateTime.UtcNow.AddDays(-1) });
        await db.SaveChangesAsync();

        var expired = await CreateService(db).ExpireUncollectedHoldsAsync();

        Assert.Equal(0, expired);
        Assert.Equal(ReservationStatus.Ready, (await db.Reservations.SingleAsync()).Status);
    }

    [Fact]
    public async Task WaitingHolds_AreNeverExpired()
    {
        using var db = TestDb.Create();
        var (item, first, _) = await SeedAsync(db);
        db.Reservations.Add(new Reservation { ItemId = item.Id, BorrowerId = first.Id, Status = ReservationStatus.Waiting, CreatedAt = DateTime.UtcNow.AddDays(-30) });
        await db.SaveChangesAsync();

        var expired = await CreateService(db).ExpireUncollectedHoldsAsync();

        Assert.Equal(0, expired);
        Assert.Equal(ReservationStatus.Waiting, (await db.Reservations.SingleAsync()).Status);
    }

    [Fact]
    public async Task ExpiredHoldWithNoQueue_LeavesTheItemAvailable()
    {
        using var db = TestDb.Create();
        var (item, first, _) = await SeedAsync(db);
        db.Reservations.Add(new Reservation { ItemId = item.Id, BorrowerId = first.Id, Status = ReservationStatus.Ready, ReadyAt = DateTime.UtcNow.AddDays(-(ReservationService.HoldDays + 1)) });
        await db.SaveChangesAsync();

        var expired = await CreateService(db).ExpireUncollectedHoldsAsync();

        Assert.Equal(1, expired);
        Assert.Equal(ItemStatus.Available, (await db.Items.SingleAsync()).Status);
        Assert.Equal(0, await db.Reservations.CountAsync(r => r.Status == ReservationStatus.Ready));
    }
}
