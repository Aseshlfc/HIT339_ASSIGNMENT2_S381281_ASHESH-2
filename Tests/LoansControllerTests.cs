using LibraryManagementSystem.Controllers;
using LibraryManagementSystem.Data;
using LibraryManagementSystem.Models;
using LibraryManagementSystem.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace LibraryManagementSystem.Tests;

public class LoansControllerTests
{
    private static async Task<(Item item, Borrower borrower)> SeedItemAndBorrowerAsync(
        LibraryContext db, string code, ItemStatus status)
    {
        var borrower = new Borrower { FullName = "Test Borrower", Email = "test@example.com", Phone = "0400000000", Address = "1 Test St" };
        var item = new Book { Name = "Test Book", Description = "A test item.", LibraryCode = code, Status = status, Author = "Author", Genre = "Genre" };
        db.Borrowers.Add(borrower);
        db.Items.Add(item);
        await db.SaveChangesAsync();
        return (item, borrower);
    }

    [Fact]
    public async Task Borrow_AvailableItem_SetsDueDateFourteenDaysOutAndMarksItemBorrowed()
    {
        using var db = TestDb.Create();
        var (item, borrower) = await SeedItemAndBorrowerAsync(db, "B900", ItemStatus.Available);
        var controller = LoansControllerFactory.Create(db);

        var result = await controller.Borrow(new BorrowViewModel { LibraryCode = "B900", BorrowerId = borrower.Id });

        Assert.IsType<RedirectToActionResult>(result);
        var updatedItem = await db.Items.SingleAsync(i => i.Id == item.Id);
        Assert.Equal(ItemStatus.Borrowed, updatedItem.Status);

        var loan = await db.Loans.SingleAsync();
        Assert.Equal(DateTime.Now.Date.AddDays(LibraryRules.LoanDays), loan.DueDate);
        Assert.Equal(borrower.Id, loan.BorrowerId);
    }

    [Fact]
    public async Task Borrow_UnavailableItem_FailsAndCreatesNoLoan()
    {
        using var db = TestDb.Create();
        var (_, borrower) = await SeedItemAndBorrowerAsync(db, "B901", ItemStatus.Borrowed);
        var controller = LoansControllerFactory.Create(db);

        var result = await controller.Borrow(new BorrowViewModel { LibraryCode = "B901", BorrowerId = borrower.Id });

        Assert.IsType<ViewResult>(result);
        Assert.False(controller.ModelState.IsValid);
        Assert.Equal(0, await db.Loans.CountAsync());
    }

    [Fact]
    public async Task Borrow_UnknownLibraryCode_FailsWithModelError()
    {
        using var db = TestDb.Create();
        var (_, borrower) = await SeedItemAndBorrowerAsync(db, "B902", ItemStatus.Available);
        var controller = LoansControllerFactory.Create(db);

        var result = await controller.Borrow(new BorrowViewModel { LibraryCode = "DOES-NOT-EXIST", BorrowerId = borrower.Id });

        Assert.IsType<ViewResult>(result);
        Assert.False(controller.ModelState.IsValid);
        Assert.Equal(0, await db.Loans.CountAsync());
    }

    [Fact]
    public async Task Return_OnTime_RecordsNoFineAndMarksReturned()
    {
        using var db = TestDb.Create();
        var (item, borrower) = await SeedItemAndBorrowerAsync(db, "B903", ItemStatus.Borrowed);
        var loan = new Loan { ItemId = item.Id, BorrowerId = borrower.Id, BorrowedAt = DateTime.Now.AddDays(-5), DueDate = DateTime.Now.Date.AddDays(1) };
        db.Loans.Add(loan);
        await db.SaveChangesAsync();

        var controller = LoansControllerFactory.Create(db);
        var result = await controller.Return(new ReturnViewModel { LoanId = loan.Id, ReturnStatus = ItemStatus.Available });

        Assert.IsType<RedirectToActionResult>(result);
        var updatedLoan = await db.Loans.SingleAsync(l => l.Id == loan.Id);
        Assert.NotNull(updatedLoan.ReturnedAt);
        Assert.Equal(0m, updatedLoan.FineAmount);

        var updatedItem = await db.Items.SingleAsync(i => i.Id == item.Id);
        Assert.Equal(ItemStatus.Available, updatedItem.Status);
    }

    [Fact]
    public async Task Return_ThreeDaysLate_CalculatesCorrectFine()
    {
        using var db = TestDb.Create();
        var (item, borrower) = await SeedItemAndBorrowerAsync(db, "B904", ItemStatus.Borrowed);
        var loan = new Loan { ItemId = item.Id, BorrowerId = borrower.Id, BorrowedAt = DateTime.Now.AddDays(-17), DueDate = DateTime.Now.Date.AddDays(-3) };
        db.Loans.Add(loan);
        await db.SaveChangesAsync();

        var controller = LoansControllerFactory.Create(db);
        await controller.Return(new ReturnViewModel { LoanId = loan.Id, ReturnStatus = ItemStatus.Available });

        var updatedLoan = await db.Loans.SingleAsync(l => l.Id == loan.Id);
        Assert.Equal(3 * LibraryRules.FinePerLateDay, updatedLoan.FineAmount);
    }

    [Fact]
    public async Task Return_DamagedCondition_SetsItemStatusToDamaged()
    {
        using var db = TestDb.Create();
        var (item, borrower) = await SeedItemAndBorrowerAsync(db, "B905", ItemStatus.Borrowed);
        var loan = new Loan { ItemId = item.Id, BorrowerId = borrower.Id, BorrowedAt = DateTime.Now.AddDays(-5), DueDate = DateTime.Now.Date.AddDays(1) };
        db.Loans.Add(loan);
        await db.SaveChangesAsync();

        var controller = LoansControllerFactory.Create(db);
        await controller.Return(new ReturnViewModel { LoanId = loan.Id, ReturnStatus = ItemStatus.Damaged });

        var updatedItem = await db.Items.SingleAsync(i => i.Id == item.Id);
        Assert.Equal(ItemStatus.Damaged, updatedItem.Status);
    }

    [Fact]
    public async Task MarkFinePaid_WithOutstandingFine_SetsFinePaidTrue()
    {
        using var db = TestDb.Create();
        var (item, borrower) = await SeedItemAndBorrowerAsync(db, "B906", ItemStatus.Available);
        var loan = new Loan
        {
            ItemId = item.Id,
            BorrowerId = borrower.Id,
            BorrowedAt = DateTime.Now.AddDays(-20),
            DueDate = DateTime.Now.Date.AddDays(-6),
            ReturnedAt = DateTime.Now,
            FineAmount = 6m,
            FinePaid = false
        };
        db.Loans.Add(loan);
        await db.SaveChangesAsync();

        var controller = LoansControllerFactory.Create(db);
        await controller.MarkFinePaid(loan.Id);

        var updatedLoan = await db.Loans.SingleAsync(l => l.Id == loan.Id);
        Assert.True(updatedLoan.FinePaid);
    }

    [Fact]
    public async Task Borrow_ConcurrentAttemptsForSameItem_OnlyOneSucceeds()
    {
        // Simulates two Reception users racing to borrow the same item: two
        // independent DbContext instances (one per simulated HTTP request),
        // both loading the item as Available before either one writes.
        //
        // This test deliberately uses a REAL SQLite database file rather than
        // the EF Core InMemory provider used by the rest of this test class.
        // The InMemory provider does not enforce optimistic concurrency
        // tokens the way a real relational provider does (it does not
        // simulate the "0 rows affected because the WHERE clause no longer
        // matches" failure mode) — an earlier version of this test against
        // InMemory let both borrow attempts silently succeed, which would
        // have been a false negative for exactly the bug this test exists to
        // catch. A real SQLite file reproduces the actual production
        // mechanism (Item.Status configured as a concurrency token in
        // LibraryContext, see LibraryContext.OnModelCreating).
        var dbPath = Path.Combine(Path.GetTempPath(), $"concurrency-test-{Guid.NewGuid():N}.db");
        // Pooling=False: Microsoft.Data.Sqlite pools connections by default, so
        // disposing a DbContext hands its connection back to the pool with the
        // file still open. Windows refuses to delete an open file, which made
        // the cleanup below throw IOException there (macOS and Linux allow
        // it). Without pooling, the file is closed as soon as each context is
        // disposed. This does not change what the test checks: the two
        // requests still use separate connections to the same database file.
        var connectionString = $"Data Source={dbPath};Pooling=False";

        static LibraryContext OpenSqlite(string cs) =>
            new(new DbContextOptionsBuilder<LibraryContext>().UseSqlite(cs).Options);

        try
        {
            int itemId, borrowerAId, borrowerBId;
            using (var seedDb = OpenSqlite(connectionString))
            {
                await seedDb.Database.EnsureCreatedAsync();
                var item = new Book { Name = "Contested Book", Description = "d", LibraryCode = "CONC1", Status = ItemStatus.Available, Author = "A", Genre = "G" };
                var borrowerA = new Borrower { FullName = "Borrower A", Email = "a@example.com", Phone = "0400000001", Address = "1 St" };
                var borrowerB = new Borrower { FullName = "Borrower B", Email = "b@example.com", Phone = "0400000002", Address = "2 St" };
                seedDb.Items.Add(item);
                seedDb.Borrowers.AddRange(borrowerA, borrowerB);
                await seedDb.SaveChangesAsync();
                itemId = item.Id;
                borrowerAId = borrowerA.Id;
                borrowerBId = borrowerB.Id;
            }

            using var dbRequestA = OpenSqlite(connectionString);
            using var dbRequestB = OpenSqlite(connectionString);

            // Both "requests" read the item as Available before either writes,
            // exactly as would happen with two near-simultaneous HTTP requests.
            _ = await dbRequestA.Items.SingleAsync(i => i.Id == itemId);
            _ = await dbRequestB.Items.SingleAsync(i => i.Id == itemId);

            var controllerA = LoansControllerFactory.Create(dbRequestA);
            var controllerB = LoansControllerFactory.Create(dbRequestB);

            var resultA = await controllerA.Borrow(new BorrowViewModel { LibraryCode = "CONC1", BorrowerId = borrowerAId });
            var resultB = await controllerB.Borrow(new BorrowViewModel { LibraryCode = "CONC1", BorrowerId = borrowerBId });

            var successCount = new IActionResult[] { resultA, resultB }.Count(r => r is RedirectToActionResult);
            Assert.Equal(1, successCount);

            // The losing request must fail gracefully (a re-rendered view with a
            // model error), not throw an unhandled exception.
            var failingResult = resultA is RedirectToActionResult ? resultB : resultA;
            Assert.IsType<ViewResult>(failingResult);

            using var verifyDb = OpenSqlite(connectionString);
            Assert.Equal(1, await verifyDb.Loans.CountAsync());
            var finalItem = await verifyDb.Items.SingleAsync(i => i.Id == itemId);
            Assert.Equal(ItemStatus.Borrowed, finalItem.Status);
        }
        finally
        {
            // Removing the temporary file is housekeeping, not part of what is
            // being tested. If the operating system still has it locked, leave
            // it in the temp folder rather than throw: an exception from this
            // finally block would replace, and so hide, a failed assertion above.
            foreach (var suffix in new[] { "", "-shm", "-wal" })
            {
                var path = dbPath + suffix;
                try
                {
                    if (File.Exists(path)) File.Delete(path);
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }

    [Fact]
    public async Task MarkFinePaid_WithZeroFine_DoesNotSetFinePaid()
    {
        using var db = TestDb.Create();
        var (item, borrower) = await SeedItemAndBorrowerAsync(db, "B907", ItemStatus.Available);
        var loan = new Loan
        {
            ItemId = item.Id,
            BorrowerId = borrower.Id,
            BorrowedAt = DateTime.Now.AddDays(-5),
            DueDate = DateTime.Now.Date.AddDays(1),
            ReturnedAt = DateTime.Now,
            FineAmount = 0m,
            FinePaid = false
        };
        db.Loans.Add(loan);
        await db.SaveChangesAsync();

        var controller = LoansControllerFactory.Create(db);
        await controller.MarkFinePaid(loan.Id);

        var updatedLoan = await db.Loans.SingleAsync(l => l.Id == loan.Id);
        Assert.False(updatedLoan.FinePaid);
    }
}
