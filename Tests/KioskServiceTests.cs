using LibraryManagementSystem.Data;
using LibraryManagementSystem.Models;
using LibraryManagementSystem.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LibraryManagementSystem.Tests;

public class LibraryCardNumberTests
{
    [Fact]
    public void CardNumber_IsTheBorrowerIdPaddedToFourDigits()
    {
        Assert.Equal("P0007", new Borrower { Id = 7 }.CardNumber);
        Assert.Equal("P0123", Borrower.FormatCardNumber(123));
        Assert.Equal("P12345", Borrower.FormatCardNumber(12345));
    }

    [Fact]
    public void ScannedOrTypedCardNumbers_AreParsedLeniently()
    {
        foreach (var input in new[] { "P0007", "p0007", " P0007 ", "p7", "7", "0007" })
        {
            Assert.True(Borrower.TryParseCardNumber(input, out var id), input);
            Assert.Equal(7, id);
        }
    }

    [Fact]
    public void InvalidCardNumbers_AreRejected()
    {
        foreach (var input in new[] { null, "", "   ", "P", "0", "P0000", "X0007", "PP7", "7A", "-7", "P12345678901", "7 7" })
            Assert.False(Borrower.TryParseCardNumber(input, out _), input ?? "(null)");
    }
}

public class KioskServiceTests
{
    private static KioskService Create(LibraryContext db) =>
        new(db, new NotificationService(db, NullLogger<NotificationService>.Instance));

    [Fact]
    public async Task FindPatron_FindsByCardNumberAndIgnoresUnknownOnes()
    {
        using var testDb = new SqliteTestDb();
        using var db = testDb.Open();
        var borrower = await Section1.AddBorrowerAsync(db);
        var kiosk = Create(db);

        Assert.Equal(borrower.Id, (await kiosk.FindPatronAsync(borrower.CardNumber))!.Id);
        Assert.Equal(borrower.Id, (await kiosk.FindPatronAsync(borrower.CardNumber.ToLowerInvariant()))!.Id);
        Assert.Null(await kiosk.FindPatronAsync("P9999"));
        Assert.Null(await kiosk.FindPatronAsync("nonsense"));
        Assert.Null(await kiosk.FindPatronAsync(null));
    }

    [Fact]
    public async Task CheckOut_LendsAnAvailableItemAtThisBranch()
    {
        using var testDb = new SqliteTestDb();
        using var db = testDb.Open();
        var (central, _, _) = await Section1.SeedBranchesAsync(db);
        var book = await Section1.AddBookAsync(db, "B100", central, name: "Kiosk Book");
        var borrower = await Section1.AddBorrowerAsync(db);

        var result = await Create(db).CheckOutAsync(central, borrower, "B100");

        Assert.True(result.Succeeded, result.Message);
        var loan = await db.Loans.SingleAsync();
        Assert.Equal(borrower.Id, loan.BorrowerId);
        Assert.Equal(book.Id, loan.ItemId);
        Assert.Equal(central.Id, loan.BranchId);
        Assert.Equal(DateTime.Now.Date.AddDays(LibraryRules.LoanDays), loan.DueDate);
        Assert.Null(loan.ReturnedAt);
        Assert.Equal(ItemStatus.Borrowed, (await db.Items.SingleAsync()).Status);
    }

    [Fact]
    public async Task CheckOut_SendsTheSameBorrowedNotificationAsTheReceptionDesk()
    {
        using var testDb = new SqliteTestDb();
        using var db = testDb.Open();
        var (central, _, _) = await Section1.SeedBranchesAsync(db);
        await Section1.AddBookAsync(db, "B100", central, name: "Kiosk Book");
        var borrower = await Section1.AddBorrowerAsync(db);

        await Create(db).CheckOutAsync(central, borrower, "B100");

        var note = await db.Notifications.SingleAsync();
        Assert.Equal(NotificationType.Borrowed, note.Type);
        Assert.Equal(borrower.Id, note.BorrowerId);
        Assert.Contains("Kiosk Book (B100)", note.Message);
    }

    [Fact]
    public async Task CheckOut_FindsTheItemWhateverTheCaseOrSpacing()
    {
        using var testDb = new SqliteTestDb();
        using var db = testDb.Open();
        var (central, _, _) = await Section1.SeedBranchesAsync(db);
        await Section1.AddBookAsync(db, "B100", central);
        var borrower = await Section1.AddBorrowerAsync(db);

        var result = await Create(db).CheckOutAsync(central, borrower, "  b100 ");

        Assert.True(result.Succeeded, result.Message);
    }

    [Fact]
    public async Task CheckOut_RejectsBlankAndUnknownCodes()
    {
        using var testDb = new SqliteTestDb();
        using var db = testDb.Open();
        var (central, _, _) = await Section1.SeedBranchesAsync(db);
        var borrower = await Section1.AddBorrowerAsync(db);
        var kiosk = Create(db);

        var blank = await kiosk.CheckOutAsync(central, borrower, "   ");
        var unknown = await kiosk.CheckOutAsync(central, borrower, "NOPE");

        Assert.False(blank.Succeeded);
        Assert.False(unknown.Succeeded);
        Assert.Contains("NOPE", unknown.Message);
        Assert.Equal(0, await db.Loans.CountAsync());
    }

    [Fact]
    public async Task CheckOut_RefusesItemsThatAreNotAvailable()
    {
        using var testDb = new SqliteTestDb();
        using var db = testDb.Open();
        var (central, _, _) = await Section1.SeedBranchesAsync(db);
        await Section1.AddBookAsync(db, "BORROWED", central, ItemStatus.Borrowed);
        await Section1.AddBookAsync(db, "DAMAGED", central, ItemStatus.Damaged);
        await Section1.AddBookAsync(db, "DESTROYED", central, ItemStatus.Destroy);
        await Section1.AddBookAsync(db, "MOVING", central, ItemStatus.InTransit);
        var borrower = await Section1.AddBorrowerAsync(db);
        var kiosk = Create(db);

        foreach (var code in new[] { "BORROWED", "DAMAGED", "DESTROYED", "MOVING" })
        {
            var result = await kiosk.CheckOutAsync(central, borrower, code);
            Assert.False(result.Succeeded, code);
        }

        Assert.Equal(0, await db.Loans.CountAsync());
        Assert.Contains("In transit", (await kiosk.CheckOutAsync(central, borrower, "MOVING")).Message);
    }

    [Fact]
    public async Task CheckOut_RefusesAnItemHeldAtAnotherBranch()
    {
        using var testDb = new SqliteTestDb();
        using var db = testDb.Open();
        var (central, north, _) = await Section1.SeedBranchesAsync(db);
        await Section1.AddBookAsync(db, "NORTH1", north);
        var borrower = await Section1.AddBorrowerAsync(db);

        var result = await Create(db).CheckOutAsync(central, borrower, "NORTH1");

        Assert.False(result.Succeeded);
        Assert.Contains("Northside Branch", result.Message);
        Assert.Equal(ItemStatus.Available, (await db.Items.SingleAsync()).Status);
        Assert.Equal(0, await db.Loans.CountAsync());
    }

    [Fact]
    public async Task CheckOut_KeepsAReadyHoldForThePatronWhoReservedIt()
    {
        using var testDb = new SqliteTestDb();
        using var db = testDb.Open();
        var (central, _, _) = await Section1.SeedBranchesAsync(db);
        var book = await Section1.AddBookAsync(db, "HELD1", central);
        var holder = await Section1.AddBorrowerAsync(db, "Holder");
        var other = await Section1.AddBorrowerAsync(db, "Someone Else");
        db.Reservations.Add(new Reservation { ItemId = book.Id, BorrowerId = holder.Id, Status = ReservationStatus.Ready, ReadyAt = DateTime.UtcNow });
        await db.SaveChangesAsync();
        var kiosk = Create(db);

        var refused = await kiosk.CheckOutAsync(central, other, "HELD1");
        var accepted = await kiosk.CheckOutAsync(central, holder, "HELD1");

        Assert.False(refused.Succeeded);
        Assert.Contains("held for another patron", refused.Message);
        Assert.True(accepted.Succeeded, accepted.Message);
        Assert.Equal(ReservationStatus.Fulfilled, (await db.Reservations.SingleAsync()).Status);
        Assert.Equal(holder.Id, (await db.Loans.SingleAsync()).BorrowerId);
    }

    [Fact]
    public async Task Account_ListsActiveLoansWithOverdueDaysAndAccruingFine()
    {
        using var testDb = new SqliteTestDb();
        using var db = testDb.Open();
        var (central, _, _) = await Section1.SeedBranchesAsync(db);
        var onTime = await Section1.AddBookAsync(db, "ONTIME", central, ItemStatus.Borrowed);
        var late = await Section1.AddBookAsync(db, "LATE", central, ItemStatus.Borrowed);
        var borrower = await Section1.AddBorrowerAsync(db);
        var today = DateTime.Now.Date;
        db.Loans.AddRange(
            new Loan { ItemId = onTime.Id, BorrowerId = borrower.Id, BorrowedAt = today.AddDays(-3), DueDate = today.AddDays(11) },
            new Loan { ItemId = late.Id, BorrowerId = borrower.Id, BorrowedAt = today.AddDays(-20), DueDate = today.AddDays(-6) });
        await db.SaveChangesAsync();

        var account = await Create(db).GetAccountAsync(borrower);

        Assert.Equal(2, account.ActiveLoans.Count);
        Assert.Equal("LATE", account.ActiveLoans[0].LibraryCode);   // earliest due date first
        Assert.Equal(6, account.ActiveLoans[0].DaysOverdue);
        Assert.True(account.ActiveLoans[0].IsOverdue);
        Assert.False(account.ActiveLoans[1].IsOverdue);
        Assert.Equal(6 * LibraryRules.FinePerLateDay, account.AccruingFineTotal);
    }

    [Fact]
    public async Task Account_ShowsUnpaidFinesButNotPaidOrZeroOnes_AndOnlyForThisPatron()
    {
        using var testDb = new SqliteTestDb();
        using var db = testDb.Open();
        var (central, _, _) = await Section1.SeedBranchesAsync(db);
        var a = await Section1.AddBookAsync(db, "A", central);
        var b = await Section1.AddBookAsync(db, "B", central);
        var c = await Section1.AddBookAsync(db, "C", central);
        var d = await Section1.AddBookAsync(db, "D", central);
        var patron = await Section1.AddBorrowerAsync(db, "Patron");
        var other = await Section1.AddBorrowerAsync(db, "Other");
        var now = DateTime.Now;
        db.Loans.AddRange(
            new Loan { ItemId = a.Id, BorrowerId = patron.Id, BorrowedAt = now.AddDays(-30), DueDate = now.AddDays(-16), ReturnedAt = now.AddDays(-12), FineAmount = 4m, FinePaid = false },
            new Loan { ItemId = b.Id, BorrowerId = patron.Id, BorrowedAt = now.AddDays(-30), DueDate = now.AddDays(-16), ReturnedAt = now.AddDays(-12), FineAmount = 2.5m, FinePaid = false },
            new Loan { ItemId = c.Id, BorrowerId = patron.Id, BorrowedAt = now.AddDays(-30), DueDate = now.AddDays(-16), ReturnedAt = now.AddDays(-12), FineAmount = 9m, FinePaid = true },
            new Loan { ItemId = d.Id, BorrowerId = patron.Id, BorrowedAt = now.AddDays(-8), DueDate = now.AddDays(6), ReturnedAt = now.AddDays(-2), FineAmount = 0m, FinePaid = false },
            new Loan { ItemId = a.Id, BorrowerId = other.Id, BorrowedAt = now.AddDays(-30), DueDate = now.AddDays(-16), ReturnedAt = now.AddDays(-12), FineAmount = 50m, FinePaid = false });
        await db.SaveChangesAsync();

        var account = await Create(db).GetAccountAsync(patron);

        Assert.Equal(2, account.UnpaidFines.Count);
        Assert.Equal(6.5m, account.UnpaidFineTotal);
        Assert.Empty(account.ActiveLoans);
    }

    [Fact]
    public async Task Account_ShowsReadyHoldsAndQueuePositions()
    {
        using var testDb = new SqliteTestDb();
        using var db = testDb.Open();
        var (central, _, _) = await Section1.SeedBranchesAsync(db);
        var wanted = await Section1.AddBookAsync(db, "WANTED", central, ItemStatus.Borrowed);
        var ready = await Section1.AddBookAsync(db, "READY", central);
        var first = await Section1.AddBorrowerAsync(db, "First");
        var second = await Section1.AddBorrowerAsync(db, "Second");
        db.Reservations.AddRange(
            new Reservation { ItemId = wanted.Id, BorrowerId = first.Id, Status = ReservationStatus.Waiting },
            new Reservation { ItemId = wanted.Id, BorrowerId = second.Id, Status = ReservationStatus.Waiting },
            new Reservation { ItemId = ready.Id, BorrowerId = second.Id, Status = ReservationStatus.Ready, ReadyAt = DateTime.UtcNow },
            new Reservation { ItemId = ready.Id, BorrowerId = second.Id, Status = ReservationStatus.Cancelled });
        await db.SaveChangesAsync();
        var kiosk = Create(db);

        var firstAccount = await kiosk.GetAccountAsync(first);
        var secondAccount = await kiosk.GetAccountAsync(second);

        Assert.Equal(1, Assert.Single(firstAccount.Holds).QueuePosition);
        Assert.Equal(2, secondAccount.Holds.Count);                  // cancelled hold is not listed
        var waiting = secondAccount.Holds.Single(h => h.Status == ReservationStatus.Waiting);
        var readyHold = secondAccount.Holds.Single(h => h.Status == ReservationStatus.Ready);
        Assert.Equal(2, waiting.QueuePosition);
        Assert.NotNull(readyHold.CollectBy);
    }
}
