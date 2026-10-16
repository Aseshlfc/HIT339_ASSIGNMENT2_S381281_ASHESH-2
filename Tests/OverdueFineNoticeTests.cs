using LibraryManagementSystem.Controllers;
using LibraryManagementSystem.Data;
using LibraryManagementSystem.Models;
using LibraryManagementSystem.Services;
using LibraryManagementSystem.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LibraryManagementSystem.Tests;

/// <summary>
/// Notices for loans that are overdue and still out, so the borrower hears about
/// the fine while it is building up and not only when the item is returned.
/// </summary>
public class OverdueFineNoticeTests
{
    private static DueDateReminderService CreateService(LibraryContext db)
    {
        var notifications = new NotificationService(db, NullLogger<NotificationService>.Instance);
        return new DueDateReminderService(db, notifications);
    }

    // Creates a borrowed book with a loan that has the given due date.
    private static async Task<Loan> AddLoanAsync(LibraryContext db, DateTime dueDate, bool returned = false,
        string email = "test@example.com", string phone = "0400000000", Borrower? borrower = null, int? loanId = null)
    {
        borrower ??= new Borrower { FullName = "Test Borrower", Email = email, Phone = phone, Address = "1 Test St" };
        var item = new Book
        {
            Name = "Test Book",
            Description = "A test item.",
            LibraryCode = "F" + Guid.NewGuid().ToString("N")[..6],
            Status = returned ? ItemStatus.Available : ItemStatus.Borrowed,
            Author = "Author",
            Genre = "Genre"
        };
        var loan = new Loan
        {
            Item = item,
            Borrower = borrower,
            BorrowedAt = dueDate.AddDays(-LibraryRules.LoanDays),
            DueDate = dueDate,
            ReturnedAt = returned ? DateTime.Now : null,
            FineAmount = returned ? Math.Max(0, (DateTime.Now.Date - dueDate.Date).Days) * LibraryRules.FinePerLateDay : 0
        };
        if (loanId.HasValue) loan.Id = loanId.Value;
        db.Loans.Add(loan);
        await db.SaveChangesAsync();
        return loan;
    }

    // Records a notice for the loan as if an earlier check had sent it the given number of days ago.
    private static async Task AddEarlierNoticeAsync(LibraryContext db, Loan loan, int daysAgo)
    {
        db.Notifications.Add(new Notification
        {
            BorrowerId = loan.BorrowerId,
            Type = NotificationType.FineAccruing,
            Channel = NotificationChannel.Email,
            Message = $"{DueDateReminderService.OverdueNoticePrefix(loan.Id)} earlier notice.",
            CreatedAt = DateTime.UtcNow.AddDays(-daysAgo)
        });
        await db.SaveChangesAsync();
    }

    private static Task<List<Notification>> AccruingNoticesAsync(LibraryContext db)
        => db.Notifications.Where(n => n.Type == NotificationType.FineAccruing).OrderBy(n => n.Id).ToListAsync();

    // ---------- which loans get a notice ----------

    [Fact]
    public async Task OverdueLoan_GetsOneFineAccruingNotice_WithTheFineSoFar()
    {
        using var db = TestDb.Create();
        var loan = await AddLoanAsync(db, DateTime.Now.Date.AddDays(-3));

        var sent = await CreateService(db).SendOverdueFineNoticesAsync();

        Assert.Equal(1, sent);
        var notice = await db.Notifications.SingleAsync();
        Assert.Equal(NotificationType.FineAccruing, notice.Type);
        Assert.Equal(NotificationChannel.Email, notice.Channel);
        Assert.Equal(loan.BorrowerId, notice.BorrowerId);
        Assert.StartsWith(DueDateReminderService.OverdueNoticePrefix(loan.Id), notice.Message);
        Assert.Contains(loan.Item.LibraryCode, notice.Message);
        Assert.Contains("3 day(s) overdue", notice.Message);
        Assert.Contains($"${3 * LibraryRules.FinePerLateDay:0.00} has built up", notice.Message);
        Assert.Contains($"{loan.DueDate:dd MMM yyyy}", notice.Message);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(6)]
    [InlineData(45)]
    public async Task FineInTheNotice_IsDaysOverdueTimesTheDailyRate(int daysOverdue)
    {
        using var db = TestDb.Create();
        await AddLoanAsync(db, DateTime.Now.Date.AddDays(-daysOverdue));

        await CreateService(db).SendOverdueFineNoticesAsync();

        var notice = await db.Notifications.SingleAsync();
        Assert.Contains($"{daysOverdue} day(s) overdue", notice.Message);
        Assert.Contains($"${daysOverdue * LibraryRules.FinePerLateDay:0.00} has built up", notice.Message);
    }

    [Fact]
    public async Task ActiveLoansThatAreNotOverdue_GetNoNotice()
    {
        using var db = TestDb.Create();
        await AddLoanAsync(db, DateTime.Now.Date);               // due today: not late yet
        await AddLoanAsync(db, DateTime.Now.Date.AddDays(1));    // due tomorrow
        await AddLoanAsync(db, DateTime.Now.Date.AddDays(10));   // due well in the future

        var sent = await CreateService(db).SendOverdueFineNoticesAsync();

        Assert.Equal(0, sent);
        Assert.Empty(await db.Notifications.ToListAsync());
    }

    [Fact]
    public async Task DueDateWithATimeOfDay_IsJudgedByCalendarDay()
    {
        using var db = TestDb.Create();
        await AddLoanAsync(db, DateTime.Now.Date.AddMinutes(23 * 60 + 59));        // later today: not overdue
        var late = await AddLoanAsync(db, DateTime.Now.Date.AddDays(-1).AddHours(9)); // yesterday morning: 1 day

        var sent = await CreateService(db).SendOverdueFineNoticesAsync();

        Assert.Equal(1, sent);
        var notice = await db.Notifications.SingleAsync();
        Assert.StartsWith(DueDateReminderService.OverdueNoticePrefix(late.Id), notice.Message);
        Assert.Contains("1 day(s) overdue", notice.Message);
        Assert.Contains($"${LibraryRules.FinePerLateDay:0.00} has built up", notice.Message);
    }

    [Fact]
    public async Task ReturnedLoan_GetsNoNotice_EvenIfItWasReturnedLate()
    {
        using var db = TestDb.Create();
        await AddLoanAsync(db, DateTime.Now.Date.AddDays(-5), returned: true);

        var sent = await CreateService(db).SendOverdueFineNoticesAsync();

        Assert.Equal(0, sent);
    }

    [Fact]
    public async Task BorrowerWithOnlyAPhone_GetsAnSmsNotice()
    {
        using var db = TestDb.Create();
        await AddLoanAsync(db, DateTime.Now.Date.AddDays(-2), email: "");

        await CreateService(db).SendOverdueFineNoticesAsync();

        Assert.Equal(NotificationChannel.Sms, (await db.Notifications.SingleAsync()).Channel);
    }

    [Fact]
    public async Task BorrowerWithNoContactDetails_IsSkipped_AndNotCounted()
    {
        using var db = TestDb.Create();
        await AddLoanAsync(db, DateTime.Now.Date.AddDays(-2), email: "", phone: "");

        var sent = await CreateService(db).SendOverdueFineNoticesAsync();

        Assert.Equal(0, sent);
        Assert.Equal(0, await db.Notifications.CountAsync());
    }

    [Fact]
    public async Task BorrowerWithTwoOverdueLoans_GetsANoticeForEach()
    {
        using var db = TestDb.Create();
        var first = await AddLoanAsync(db, DateTime.Now.Date.AddDays(-2));
        var second = await AddLoanAsync(db, DateTime.Now.Date.AddDays(-9), borrower: first.Borrower);

        var sent = await CreateService(db).SendOverdueFineNoticesAsync();

        Assert.Equal(2, sent);
        var notices = await AccruingNoticesAsync(db);
        Assert.Contains(notices, n => n.Message.StartsWith(DueDateReminderService.OverdueNoticePrefix(first.Id)) && n.Message.Contains("$2.00"));
        Assert.Contains(notices, n => n.Message.StartsWith(DueDateReminderService.OverdueNoticePrefix(second.Id)) && n.Message.Contains("$9.00"));
    }

    // ---------- how often ----------

    [Fact]
    public async Task RunningTheCheckAgainTheSameDay_DoesNotSendADuplicate()
    {
        using var db = TestDb.Create();
        await AddLoanAsync(db, DateTime.Now.Date.AddDays(-3));
        var service = CreateService(db);

        await service.SendOverdueFineNoticesAsync();
        var secondRun = await service.SendOverdueFineNoticesAsync();
        var thirdRun = await service.SendOverdueFineNoticesAsync();

        Assert.Equal(0, secondRun);
        Assert.Equal(0, thirdRun);
        Assert.Single(await AccruingNoticesAsync(db));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(DueDateReminderService.OverdueNoticeIntervalDays - 1)]
    public async Task ANoticeSentWithinTheInterval_StopsAnotherOne(int daysAgo)
    {
        using var db = TestDb.Create();
        var loan = await AddLoanAsync(db, DateTime.Now.Date.AddDays(-10));
        await AddEarlierNoticeAsync(db, loan, daysAgo);

        var sent = await CreateService(db).SendOverdueFineNoticesAsync();

        Assert.Equal(0, sent);
    }

    [Theory]
    [InlineData(DueDateReminderService.OverdueNoticeIntervalDays)]
    [InlineData(DueDateReminderService.OverdueNoticeIntervalDays + 3)]
    public async Task OnceTheIntervalHasPassed_AFollowUpNoticeShowsTheLargerFine(int daysAgo)
    {
        using var db = TestDb.Create();
        var loan = await AddLoanAsync(db, DateTime.Now.Date.AddDays(-12));
        await AddEarlierNoticeAsync(db, loan, daysAgo);

        var sent = await CreateService(db).SendOverdueFineNoticesAsync();

        Assert.Equal(1, sent);
        var latest = (await AccruingNoticesAsync(db)).Last();
        Assert.Contains("12 day(s) overdue", latest.Message);
        Assert.Contains($"${12 * LibraryRules.FinePerLateDay:0.00} has built up", latest.Message);
    }

    [Fact]
    public async Task ANoticeForOneLoan_DoesNotStopTheNoticeForAnother()
    {
        using var db = TestDb.Create();
        // Loan #1 and loan #12: the prefix for #1 must not match notices for #12.
        var loanOne = await AddLoanAsync(db, DateTime.Now.Date.AddDays(-4), loanId: 1);
        var loanTwelve = await AddLoanAsync(db, DateTime.Now.Date.AddDays(-4), borrower: loanOne.Borrower, loanId: 12);
        await AddEarlierNoticeAsync(db, loanTwelve, daysAgo: 1);

        var sent = await CreateService(db).SendOverdueFineNoticesAsync();

        Assert.Equal(1, sent);
        var newest = (await AccruingNoticesAsync(db)).Last();
        Assert.StartsWith(DueDateReminderService.OverdueNoticePrefix(1), newest.Message);
    }

    [Fact]
    public async Task OtherNotificationTypesForTheBorrower_DoNotStopTheNotice()
    {
        using var db = TestDb.Create();
        var loan = await AddLoanAsync(db, DateTime.Now.Date.AddDays(-3));
        var notifications = new NotificationService(db, NullLogger<NotificationService>.Instance);
        await notifications.SendAsync(loan.BorrowerId, NotificationType.Borrowed, "You borrowed a book.");
        await notifications.SendAsync(loan.BorrowerId, NotificationType.FineIssued,
            $"{DueDateReminderService.OverdueNoticePrefix(loan.Id)} not a FineAccruing notice.");

        var sent = await CreateService(db).SendOverdueFineNoticesAsync();

        Assert.Equal(1, sent);
    }

    // ---------- working alongside the existing notifications ----------

    [Fact]
    public async Task DueSoonAndOverdueChecks_EachHandleOnlyTheirOwnLoans()
    {
        using var db = TestDb.Create();
        await AddLoanAsync(db, DateTime.Now.Date.AddDays(1));    // due soon
        await AddLoanAsync(db, DateTime.Now.Date.AddDays(-3));   // overdue
        var service = CreateService(db);

        var dueSoon = await service.SendDueSoonRemindersAsync();
        var overdue = await service.SendOverdueFineNoticesAsync();

        Assert.Equal(1, dueSoon);
        Assert.Equal(1, overdue);
        Assert.Equal(1, await db.Notifications.CountAsync(n => n.Type == NotificationType.DueSoon));
        Assert.Equal(1, await db.Notifications.CountAsync(n => n.Type == NotificationType.FineAccruing));
    }

    [Fact]
    public async Task AfterTheItemIsReturned_TheFineIsIssuedForTheSameAmount_AndNoticesStop()
    {
        using var db = TestDb.Create();
        var loan = await AddLoanAsync(db, DateTime.Now.Date.AddDays(-4));
        var service = CreateService(db);
        await service.SendOverdueFineNoticesAsync();

        var loans = LoansControllerFactory.Create(db);
        ControllerTestHelpers.AttachContext(loans);
        await loans.Return(new ReturnViewModel { LoanId = loan.Id, ReturnStatus = ItemStatus.Available });

        // The return charges what the notice said had built up.
        var returned = await db.Loans.AsNoTracking().SingleAsync(l => l.Id == loan.Id);
        Assert.Equal(4 * LibraryRules.FinePerLateDay, returned.FineAmount);
        Assert.Contains("$4.00", (await db.Notifications.SingleAsync(n => n.Type == NotificationType.FineIssued)).Message);
        Assert.Contains("$4.00", (await AccruingNoticesAsync(db)).Single().Message);

        // Even after the interval, a returned loan gets no further notices.
        foreach (var n in db.Notifications) n.CreatedAt = n.CreatedAt.AddDays(-30);
        await db.SaveChangesAsync();
        Assert.Equal(0, await service.SendOverdueFineNoticesAsync());
    }

    [Fact]
    public async Task StaffDueDateCheck_SendsRemindersAndOverdueNotices_AndReportsBoth()
    {
        using var db = TestDb.Create();
        await AddLoanAsync(db, DateTime.Now.Date.AddDays(2));    // due soon
        await AddLoanAsync(db, DateTime.Now.Date.AddDays(-1));   // overdue
        await AddLoanAsync(db, DateTime.Now.Date.AddDays(-20));  // overdue
        var controller = new NotificationsController(db, CreateService(db));
        ControllerTestHelpers.AttachContext(controller);

        var result = await controller.RunDueDateCheck();

        Assert.Equal(nameof(NotificationsController.Index), Assert.IsType<RedirectToActionResult>(result).ActionName);
        Assert.Equal("Due-date check finished. 1 due-soon reminder(s) and 2 overdue fine notice(s) sent.", controller.TempData["Success"]);

        await controller.RunDueDateCheck();
        Assert.Equal("Due-date check finished. No new reminders or overdue notices were needed.", controller.TempData["Success"]);
        Assert.Equal(3, await db.Notifications.CountAsync());
    }

    // ---------- on the real database ----------

    [Fact]
    public async Task OnSqlite_RepeatedChecksDoNotDuplicate_AndTheIntervalStillApplies()
    {
        // SQLite returns dates without a UTC marker and translates StartsWith
        // to SQL, so the rules are checked there as well as in memory.
        using var sqlite = new SqliteTestDb();
        int loanId;
        using (var db = sqlite.Open())
        {
            loanId = (await AddLoanAsync(db, DateTime.Now.Date.AddDays(-5))).Id;
        }

        using (var db = sqlite.Open())
        {
            Assert.Equal(1, await CreateService(db).SendOverdueFineNoticesAsync());
        }
        using (var db = sqlite.Open())
        {
            Assert.Equal(0, await CreateService(db).SendOverdueFineNoticesAsync());
        }

        using (var db = sqlite.Open())
        {
            var notice = await db.Notifications.SingleAsync();
            notice.CreatedAt = notice.CreatedAt.AddDays(-DueDateReminderService.OverdueNoticeIntervalDays);
            await db.SaveChangesAsync();
        }
        using (var db = sqlite.Open())
        {
            Assert.Equal(1, await CreateService(db).SendOverdueFineNoticesAsync());
            var notices = await db.Notifications.Where(n => n.Type == NotificationType.FineAccruing).ToListAsync();
            Assert.Equal(2, notices.Count);
            Assert.All(notices, n => Assert.StartsWith(DueDateReminderService.OverdueNoticePrefix(loanId), n.Message));
        }
    }
}
