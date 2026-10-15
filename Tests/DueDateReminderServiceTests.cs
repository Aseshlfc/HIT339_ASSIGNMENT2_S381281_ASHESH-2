using LibraryManagementSystem.Data;
using LibraryManagementSystem.Models;
using LibraryManagementSystem.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LibraryManagementSystem.Tests;

public class DueDateReminderServiceTests
{
    private static DueDateReminderService CreateService(LibraryContext db)
    {
        var notifications = new NotificationService(db, NullLogger<NotificationService>.Instance);
        return new DueDateReminderService(db, notifications);
    }

    // Creates a borrowed book with a loan that has the given due date.
    private static async Task AddLoanAsync(LibraryContext db, DateTime dueDate, bool returned = false,
        string email = "test@example.com", string phone = "0400000000")
    {
        var borrower = new Borrower { FullName = "Test Borrower", Email = email, Phone = phone, Address = "1 Test St" };
        var item = new Book
        {
            Name = "Test Book",
            Description = "A test item.",
            LibraryCode = "R" + Guid.NewGuid().ToString("N")[..6],
            Status = ItemStatus.Borrowed,
            Author = "Author",
            Genre = "Genre"
        };
        db.Loans.Add(new Loan
        {
            Item = item,
            Borrower = borrower,
            BorrowedAt = dueDate.AddDays(-LibraryRules.LoanDays),
            DueDate = dueDate,
            ReturnedAt = returned ? DateTime.Now : null
        });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task LoanDueTomorrow_GetsOneDueSoonReminder()
    {
        using var db = TestDb.Create();
        await AddLoanAsync(db, DateTime.Now.Date.AddDays(1));

        var sent = await CreateService(db).SendDueSoonRemindersAsync();

        Assert.Equal(1, sent);
        var notification = await db.Notifications.SingleAsync();
        Assert.Equal(NotificationType.DueSoon, notification.Type);
    }

    [Fact]
    public async Task RunningTheCheckTwice_DoesNotSendDuplicateReminders()
    {
        using var db = TestDb.Create();
        await AddLoanAsync(db, DateTime.Now.Date.AddDays(1));
        var service = CreateService(db);

        await service.SendDueSoonRemindersAsync();
        var secondRun = await service.SendDueSoonRemindersAsync();

        Assert.Equal(0, secondRun);
        Assert.Equal(1, await db.Notifications.CountAsync());
    }

    [Fact]
    public async Task LoansOutsideTheWindow_GetNoReminder()
    {
        using var db = TestDb.Create();
        await AddLoanAsync(db, DateTime.Now.Date.AddDays(10));   // due too far away
        await AddLoanAsync(db, DateTime.Now.Date.AddDays(-3));   // already overdue

        var sent = await CreateService(db).SendDueSoonRemindersAsync();

        Assert.Equal(0, sent);
    }

    [Fact]
    public async Task ReturnedLoan_GetsNoReminder()
    {
        using var db = TestDb.Create();
        await AddLoanAsync(db, DateTime.Now.Date.AddDays(1), returned: true);

        var sent = await CreateService(db).SendDueSoonRemindersAsync();

        Assert.Equal(0, sent);
    }

    [Fact]
    public async Task BorrowerWithNoContactDetails_IsSkipped()
    {
        using var db = TestDb.Create();
        await AddLoanAsync(db, DateTime.Now.Date.AddDays(1), email: "", phone: "");

        var sent = await CreateService(db).SendDueSoonRemindersAsync();

        Assert.Equal(0, sent);
    }
}
