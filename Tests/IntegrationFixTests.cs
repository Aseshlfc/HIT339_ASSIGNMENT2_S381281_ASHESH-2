using System.Reflection;
using LibraryManagementSystem.Api;
using LibraryManagementSystem.Controllers;
using LibraryManagementSystem.Data;
using LibraryManagementSystem.Models;
using LibraryManagementSystem.Services;
using LibraryManagementSystem.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace LibraryManagementSystem.Tests;

/// <summary>
/// Regression tests for the problems found when Sections 1, 2 and 3 were combined:
/// reservations against transfers, the reservation queue at the desk and the kiosk,
/// written-off items, hold cancellation, kiosk rate limiting, API status values and
/// PDF export of names the report font cannot draw.
/// </summary>
public class IntegrationFixTests
{
    private static NotificationService Notifier(LibraryContext db) => new(db, NullLogger<NotificationService>.Instance);
    private static ReservationService Reservations(LibraryContext db) => new(db, Notifier(db));

    private static async Task<Borrower> AddBorrowerAsync(LibraryContext db, string name)
    {
        var borrower = new Borrower { FullName = name, Email = $"{name.Replace(" ", "").ToLowerInvariant()}@example.com", Phone = "0400000000", Address = "1 Test St" };
        db.Borrowers.Add(borrower);
        await db.SaveChangesAsync();
        return borrower;
    }

    private static async Task<Reservation> AddHoldAsync(LibraryContext db, Item item, Borrower borrower, ReservationStatus status = ReservationStatus.Waiting)
    {
        var hold = new Reservation { ItemId = item.Id, BorrowerId = borrower.Id, Status = status };
        if (status == ReservationStatus.Ready) hold.ReadyAt = DateTime.UtcNow;
        db.Reservations.Add(hold);
        await db.SaveChangesAsync();
        return hold;
    }

    private static async Task<ReservationStatus> StatusOfAsync(LibraryContext db, Reservation hold) =>
        (await db.Reservations.AsNoTracking().SingleAsync(r => r.Id == hold.Id)).Status;

    private static TransfersController Transfers(LibraryContext db)
    {
        var controller = new TransfersController(db, new TransferService(db), Reservations(db));
        Section1.SignIn(controller, "admin", "Admin");
        return controller;
    }

    // ---------- Issue 1: a reserved item must not be transferred away

    [Fact]
    public async Task Transfer_ItemWithReadyHold_CannotBeRequested()
    {
        using var db = TestDb.Create();
        var (central, north, _) = await Section1.SeedBranchesAsync(db);
        var book = await Section1.AddBookAsync(db, "B1", central);
        await AddHoldAsync(db, book, await AddBorrowerAsync(db, "Ann Holder"), ReservationStatus.Ready);

        var result = await new TransferService(db).RequestAsync(book.Id, north.Id, null, "admin");

        Assert.False(result.Succeeded);
        Assert.Contains("reserved", result.Message);
        Assert.Equal(0, await db.ItemTransfers.CountAsync());
    }

    [Fact]
    public async Task Transfer_HoldPlacedAfterRequest_BlocksDispatch()
    {
        using var db = TestDb.Create();
        var (central, north, _) = await Section1.SeedBranchesAsync(db);
        var book = await Section1.AddBookAsync(db, "B1", central);
        var service = new TransferService(db);
        var id = (await service.RequestAsync(book.Id, north.Id, null, "admin")).Transfer!.Id;
        await AddHoldAsync(db, book, await AddBorrowerAsync(db, "Ann Holder"), ReservationStatus.Ready);

        var result = await service.DispatchAsync(id, "admin");

        Assert.False(result.Succeeded);
        Assert.Equal(ItemStatus.Available, (await db.Items.SingleAsync()).Status);
    }

    [Fact]
    public async Task Transfer_ItemWithoutHolds_CanStillBeRequestedAndDispatched()
    {
        using var db = TestDb.Create();
        var (central, north, _) = await Section1.SeedBranchesAsync(db);
        var book = await Section1.AddBookAsync(db, "B1", central);
        var borrower = await AddBorrowerAsync(db, "Old Holder");
        // Finished holds do not count.
        await AddHoldAsync(db, book, borrower, ReservationStatus.Fulfilled);
        await AddHoldAsync(db, book, borrower, ReservationStatus.Cancelled);
        await AddHoldAsync(db, book, borrower, ReservationStatus.Expired);
        var service = new TransferService(db);

        var requested = await service.RequestAsync(book.Id, north.Id, null, "admin");
        var dispatched = await service.DispatchAsync(requested.Transfer!.Id, "admin");

        Assert.True(requested.Succeeded);
        Assert.True(dispatched.Succeeded);
    }

    // ---------- Issue 2: an item coming back on the shelf through a transfer is offered to the queue

    [Fact]
    public async Task TransferReceive_OffersTheItemToTheFirstWaitingBorrower()
    {
        using var db = TestDb.Create();
        var (central, north, _) = await Section1.SeedBranchesAsync(db);
        var book = await Section1.AddBookAsync(db, "B1", central);
        var service = new TransferService(db);
        var id = (await service.RequestAsync(book.Id, north.Id, null, "admin")).Transfer!.Id;
        await service.DispatchAsync(id, "admin");
        // Holds can be waiting while the item travels (they were placed while it was on loan
        // in real use; inserted directly here to set up that state).
        var first = await AddHoldAsync(db, book, await AddBorrowerAsync(db, "First Waiting"));
        var second = await AddHoldAsync(db, book, await AddBorrowerAsync(db, "Second Waiting"));

        await Transfers(db).Receive(id);

        Assert.Equal(ItemStatus.Available, (await db.Items.SingleAsync()).Status);
        Assert.Equal(ReservationStatus.Ready, await StatusOfAsync(db, first));
        Assert.Equal(ReservationStatus.Waiting, await StatusOfAsync(db, second));
        var notification = await db.Notifications.SingleAsync();
        Assert.Equal(NotificationType.ReservationReady, notification.Type);
        Assert.Equal(first.BorrowerId, notification.BorrowerId);
    }

    [Fact]
    public async Task TransferCancelAfterDispatch_OffersTheItemToTheFirstWaitingBorrower()
    {
        using var db = TestDb.Create();
        var (central, north, _) = await Section1.SeedBranchesAsync(db);
        var book = await Section1.AddBookAsync(db, "B1", central);
        var service = new TransferService(db);
        var id = (await service.RequestAsync(book.Id, north.Id, null, "admin")).Transfer!.Id;
        await service.DispatchAsync(id, "admin");
        var waiting = await AddHoldAsync(db, book, await AddBorrowerAsync(db, "First Waiting"));

        await Transfers(db).Cancel(id);

        Assert.Equal(ItemStatus.Available, (await db.Items.SingleAsync()).Status);
        Assert.Equal(ReservationStatus.Ready, await StatusOfAsync(db, waiting));
        Assert.Equal(1, await db.Notifications.CountAsync(n => n.Type == NotificationType.ReservationReady));
    }

    // ---------- Issue 3: nobody borrows past the queue, at the desk or at the kiosk

    [Fact]
    public async Task DeskBorrow_WalkInCannotTakeAnAvailableItemPastAWaitingQueue()
    {
        using var db = TestDb.Create();
        var (central, _, _) = await Section1.SeedBranchesAsync(db);
        var book = await Section1.AddBookAsync(db, "B1", central);
        var waiting = await AddHoldAsync(db, book, await AddBorrowerAsync(db, "First Waiting"));
        var walkIn = await AddBorrowerAsync(db, "Walk In");
        var controller = LoansControllerFactory.Create(db);

        await controller.Borrow(new BorrowViewModel { LibraryCode = "B1", BorrowerId = walkIn.Id });

        Assert.False(controller.ModelState.IsValid);
        Assert.Equal(0, await db.Loans.CountAsync());
        Assert.Equal(ItemStatus.Available, (await db.Items.SingleAsync()).Status);
        // The attempt promoted the borrower who was first in line, and told them.
        Assert.Equal(ReservationStatus.Ready, await StatusOfAsync(db, waiting));
        Assert.Equal(1, await db.Notifications.CountAsync(n => n.Type == NotificationType.ReservationReady));
    }

    [Fact]
    public async Task DeskBorrow_FirstWaitingBorrowerCanBorrow_AndTheHoldIsFulfilled()
    {
        using var db = TestDb.Create();
        var (central, _, _) = await Section1.SeedBranchesAsync(db);
        var book = await Section1.AddBookAsync(db, "B1", central);
        var first = await AddBorrowerAsync(db, "First Waiting");
        var hold = await AddHoldAsync(db, book, first);
        var later = await AddHoldAsync(db, book, await AddBorrowerAsync(db, "Second Waiting"));

        await LoansControllerFactory.Create(db).Borrow(new BorrowViewModel { LibraryCode = "B1", BorrowerId = first.Id });

        Assert.Equal(first.Id, (await db.Loans.SingleAsync()).BorrowerId);
        Assert.Equal(ReservationStatus.Fulfilled, await StatusOfAsync(db, hold));
        Assert.Equal(ReservationStatus.Waiting, await StatusOfAsync(db, later));
    }

    [Fact]
    public async Task DeskBorrow_SecondInQueueCannotBorrowAheadOfTheFirst()
    {
        using var db = TestDb.Create();
        var (central, _, _) = await Section1.SeedBranchesAsync(db);
        var book = await Section1.AddBookAsync(db, "B1", central);
        await AddHoldAsync(db, book, await AddBorrowerAsync(db, "First Waiting"));
        var second = await AddBorrowerAsync(db, "Second Waiting");
        await AddHoldAsync(db, book, second);
        var controller = LoansControllerFactory.Create(db);

        await controller.Borrow(new BorrowViewModel { LibraryCode = "B1", BorrowerId = second.Id });

        Assert.False(controller.ModelState.IsValid);
        Assert.Equal(0, await db.Loans.CountAsync());
    }

    [Fact]
    public async Task DeskBorrow_ItemWithNoQueue_IsUnaffected()
    {
        using var db = TestDb.Create();
        var (central, _, _) = await Section1.SeedBranchesAsync(db);
        await Section1.AddBookAsync(db, "B1", central);
        var borrower = await AddBorrowerAsync(db, "Walk In");

        await LoansControllerFactory.Create(db).Borrow(new BorrowViewModel { LibraryCode = "B1", BorrowerId = borrower.Id });

        Assert.Equal(1, await db.Loans.CountAsync());
    }

    [Fact]
    public async Task DeskBorrow_AcceptsACodeTypedInLowerCaseWithSpaces()
    {
        using var db = TestDb.Create();
        var (central, _, _) = await Section1.SeedBranchesAsync(db);
        await Section1.AddBookAsync(db, "B1", central);
        var borrower = await AddBorrowerAsync(db, "Walk In");

        await LoansControllerFactory.Create(db).Borrow(new BorrowViewModel { LibraryCode = " b1 ", BorrowerId = borrower.Id });

        Assert.Equal(1, await db.Loans.CountAsync());
    }

    [Fact]
    public async Task KioskCheckout_WalkInCannotTakeAnAvailableItemPastAWaitingQueue()
    {
        using var db = TestDb.Create();
        var (central, _, _) = await Section1.SeedBranchesAsync(db);
        var book = await Section1.AddBookAsync(db, "B1", central);
        var waiting = await AddHoldAsync(db, book, await AddBorrowerAsync(db, "First Waiting"));
        var walkIn = await AddBorrowerAsync(db, "Walk In");

        var result = await new KioskService(db, Notifier(db)).CheckOutAsync(central, walkIn, "B1");

        Assert.False(result.Succeeded);
        Assert.Contains("held for another patron", result.Message);
        Assert.Equal(0, await db.Loans.CountAsync());
        Assert.Equal(ReservationStatus.Ready, await StatusOfAsync(db, waiting));
    }

    [Fact]
    public async Task KioskCheckout_FirstWaitingPatronCanBorrow_AndTheHoldIsFulfilled()
    {
        using var db = TestDb.Create();
        var (central, _, _) = await Section1.SeedBranchesAsync(db);
        var book = await Section1.AddBookAsync(db, "B1", central);
        var first = await AddBorrowerAsync(db, "First Waiting");
        var hold = await AddHoldAsync(db, book, first);

        var result = await new KioskService(db, Notifier(db)).CheckOutAsync(central, first, "B1");

        Assert.True(result.Succeeded);
        Assert.Equal(ReservationStatus.Fulfilled, await StatusOfAsync(db, hold));
    }

    // ---------- Issue 4: a written-off item leaves no borrower waiting

    [Fact]
    public async Task ReturnAsDestroy_CancelsEveryActiveHold_AndTellsEachBorrower()
    {
        using var db = TestDb.Create();
        var (central, _, _) = await Section1.SeedBranchesAsync(db);
        var book = await Section1.AddBookAsync(db, "B1", central);
        var reader = await AddBorrowerAsync(db, "Current Reader");
        await LoansControllerFactory.Create(db).Borrow(new BorrowViewModel { LibraryCode = "B1", BorrowerId = reader.Id });
        var first = await AddHoldAsync(db, book, await AddBorrowerAsync(db, "First Waiting"));
        var second = await AddHoldAsync(db, book, await AddBorrowerAsync(db, "Second Waiting"));
        var loan = await db.Loans.SingleAsync();

        await LoansControllerFactory.Create(db).Return(new ReturnViewModel { LoanId = loan.Id, ReturnStatus = ItemStatus.Destroy });

        Assert.Equal(ItemStatus.Destroy, (await db.Items.SingleAsync()).Status);
        Assert.Equal(ReservationStatus.Cancelled, await StatusOfAsync(db, first));
        Assert.Equal(ReservationStatus.Cancelled, await StatusOfAsync(db, second));
        var cancelled = await db.Notifications.Where(n => n.Type == NotificationType.ReservationCancelled).ToListAsync();
        Assert.Equal(new[] { first.BorrowerId, second.BorrowerId }.OrderBy(i => i), cancelled.Select(n => n.BorrowerId).OrderBy(i => i));
    }

    [Fact]
    public async Task ReturnAsDamaged_KeepsTheQueue()
    {
        using var db = TestDb.Create();
        var (central, _, _) = await Section1.SeedBranchesAsync(db);
        var book = await Section1.AddBookAsync(db, "B1", central);
        var reader = await AddBorrowerAsync(db, "Current Reader");
        await LoansControllerFactory.Create(db).Borrow(new BorrowViewModel { LibraryCode = "B1", BorrowerId = reader.Id });
        var waiting = await AddHoldAsync(db, book, await AddBorrowerAsync(db, "First Waiting"));
        var loan = await db.Loans.SingleAsync();

        await LoansControllerFactory.Create(db).Return(new ReturnViewModel { LoanId = loan.Id, ReturnStatus = ItemStatus.Damaged });

        // A damaged item may be repaired, so the borrower keeps their place.
        Assert.Equal(ReservationStatus.Waiting, await StatusOfAsync(db, waiting));
        Assert.Equal(0, await db.Notifications.CountAsync(n => n.Type == NotificationType.ReservationCancelled));
    }

    [Fact]
    public async Task AdminMarksAHeldItemDestroy_CancelsTheReadyHold()
    {
        using var db = TestDb.Create();
        var (central, _, _) = await Section1.SeedBranchesAsync(db);
        var book = await Section1.AddBookAsync(db, "B1", central);
        var hold = await AddHoldAsync(db, book, await AddBorrowerAsync(db, "Ready Holder"), ReservationStatus.Ready);
        var controller = new ItemsController(db, null, Reservations(db));

        await controller.Edit(new ItemFormViewModel { Id = book.Id, ItemType = "Book", Name = book.Name, Description = "d", LibraryCode = "B1", Status = ItemStatus.Destroy, Author = "A", BookGenre = "G", BranchId = central.Id });

        Assert.Equal(ItemStatus.Destroy, (await db.Items.SingleAsync()).Status);
        Assert.Equal(ReservationStatus.Cancelled, await StatusOfAsync(db, hold));
        Assert.Equal(1, await db.Notifications.CountAsync(n => n.Type == NotificationType.ReservationCancelled));
    }

    [Fact]
    public async Task AdminRepairsADamagedItem_MakesTheFirstHoldReady()
    {
        using var db = TestDb.Create();
        var (central, _, _) = await Section1.SeedBranchesAsync(db);
        var book = await Section1.AddBookAsync(db, "B1", central, ItemStatus.Damaged);
        var hold = await AddHoldAsync(db, book, await AddBorrowerAsync(db, "First Waiting"));
        var controller = new ItemsController(db, null, Reservations(db));

        await controller.Edit(new ItemFormViewModel { Id = book.Id, ItemType = "Book", Name = book.Name, Description = "d", LibraryCode = "B1", Status = ItemStatus.Available, Author = "A", BookGenre = "G", BranchId = central.Id });

        Assert.Equal(ReservationStatus.Ready, await StatusOfAsync(db, hold));
    }

    // ---------- Hold placement and cancellation through the controller

    private static ReservationsController ReservationsController(LibraryContext db)
    {
        var controller = new ReservationsController(db, Reservations(db));
        Section1.SignIn(controller, "reception", "Reception");
        return controller;
    }

    [Fact]
    public async Task PlaceHold_OnABorrowedItem_Succeeds_AndAcceptsALowerCaseCode()
    {
        using var db = TestDb.Create();
        var (central, _, _) = await Section1.SeedBranchesAsync(db);
        var book = await Section1.AddBookAsync(db, "B1", central, ItemStatus.Borrowed);
        var borrower = await AddBorrowerAsync(db, "Wants It");

        var result = await ReservationsController(db).Create(new ReserveViewModel { LibraryCode = " b1 ", BorrowerId = borrower.Id });

        Assert.IsType<RedirectToActionResult>(result);
        var hold = await db.Reservations.SingleAsync();
        Assert.Equal(book.Id, hold.ItemId);
        Assert.Equal(ReservationStatus.Waiting, hold.Status);
    }

    [Theory]
    [InlineData(ItemStatus.Available)]
    [InlineData(ItemStatus.Destroy)]
    [InlineData(ItemStatus.InTransit)]
    public async Task PlaceHold_IsRefusedForItemsThatCannotBeQueuedFor(ItemStatus status)
    {
        using var db = TestDb.Create();
        var (central, _, _) = await Section1.SeedBranchesAsync(db);
        await Section1.AddBookAsync(db, "B1", central, status);
        var borrower = await AddBorrowerAsync(db, "Wants It");
        var controller = ReservationsController(db);

        await controller.Create(new ReserveViewModel { LibraryCode = "B1", BorrowerId = borrower.Id });

        Assert.False(controller.ModelState.IsValid);
        Assert.Equal(0, await db.Reservations.CountAsync());
    }

    [Fact]
    public async Task PlaceHold_Twice_ForTheSameBorrower_IsRefused()
    {
        using var db = TestDb.Create();
        var (central, _, _) = await Section1.SeedBranchesAsync(db);
        await Section1.AddBookAsync(db, "B1", central, ItemStatus.Borrowed);
        var borrower = await AddBorrowerAsync(db, "Wants It");
        await ReservationsController(db).Create(new ReserveViewModel { LibraryCode = "B1", BorrowerId = borrower.Id });
        var second = ReservationsController(db);

        await second.Create(new ReserveViewModel { LibraryCode = "B1", BorrowerId = borrower.Id });

        Assert.False(second.ModelState.IsValid);
        Assert.Equal(1, await db.Reservations.CountAsync());
    }

    [Fact]
    public async Task CancelReadyHold_ReleasesTheItemToTheNextBorrower()
    {
        using var db = TestDb.Create();
        var (central, _, _) = await Section1.SeedBranchesAsync(db);
        var book = await Section1.AddBookAsync(db, "B1", central);
        var ready = await AddHoldAsync(db, book, await AddBorrowerAsync(db, "Ready Holder"), ReservationStatus.Ready);
        var next = await AddHoldAsync(db, book, await AddBorrowerAsync(db, "Next In Line"));

        await ReservationsController(db).Cancel(ready.Id);

        Assert.Equal(ReservationStatus.Cancelled, await StatusOfAsync(db, ready));
        Assert.Equal(ReservationStatus.Ready, await StatusOfAsync(db, next));
        var notification = await db.Notifications.SingleAsync();
        Assert.Equal(next.BorrowerId, notification.BorrowerId);
    }

    [Fact]
    public async Task CancelWaitingHold_LeavesTheRestOfTheQueueAlone()
    {
        using var db = TestDb.Create();
        var (central, _, _) = await Section1.SeedBranchesAsync(db);
        var book = await Section1.AddBookAsync(db, "B1", central, ItemStatus.Borrowed);
        var first = await AddHoldAsync(db, book, await AddBorrowerAsync(db, "First Waiting"));
        var second = await AddHoldAsync(db, book, await AddBorrowerAsync(db, "Second Waiting"));

        await ReservationsController(db).Cancel(first.Id);

        Assert.Equal(ReservationStatus.Cancelled, await StatusOfAsync(db, first));
        Assert.Equal(ReservationStatus.Waiting, await StatusOfAsync(db, second));
        Assert.Equal(0, await db.Notifications.CountAsync());
    }

    [Fact]
    public async Task CancelFulfilledHold_IsRefused()
    {
        using var db = TestDb.Create();
        var (central, _, _) = await Section1.SeedBranchesAsync(db);
        var book = await Section1.AddBookAsync(db, "B1", central, ItemStatus.Borrowed);
        var done = await AddHoldAsync(db, book, await AddBorrowerAsync(db, "Already Has It"), ReservationStatus.Fulfilled);
        var controller = ReservationsController(db);

        await controller.Cancel(done.Id);

        Assert.Equal(ReservationStatus.Fulfilled, await StatusOfAsync(db, done));
        Assert.NotNull(controller.TempData["Error"]);
    }

    // ---------- A Ready hold whose item leaves the shelf goes back to the front of the queue

    private static ItemFormViewModel BookForm(Item book, Branch branch, ItemStatus status) => new()
    {
        Id = book.Id, ItemType = "Book", Name = book.Name, Description = "d", LibraryCode = book.LibraryCode,
        Status = status, Author = "A", BookGenre = "G", BranchId = branch.Id
    };

    [Fact]
    public async Task ReadyHold_ItemMarkedDamaged_GoesBackToWaitingAndTheBorrowerIsTold()
    {
        using var db = TestDb.Create();
        var (central, _, _) = await Section1.SeedBranchesAsync(db);
        var book = await Section1.AddBookAsync(db, "B1", central);
        var ready = await AddHoldAsync(db, book, await AddBorrowerAsync(db, "Ready Holder"), ReservationStatus.Ready);
        var next = await AddHoldAsync(db, book, await AddBorrowerAsync(db, "Next In Line"));

        await new ItemsController(db, null, Reservations(db)).Edit(BookForm(book, central, ItemStatus.Damaged));

        var hold = await db.Reservations.AsNoTracking().SingleAsync(r => r.Id == ready.Id);
        Assert.Equal(ReservationStatus.Waiting, hold.Status);
        Assert.Null(hold.ReadyAt);
        Assert.Equal(ReservationStatus.Waiting, await StatusOfAsync(db, next));
        var notification = await db.Notifications.SingleAsync();
        Assert.Equal(NotificationType.ReservationDelayed, notification.Type);
        Assert.Equal(ready.BorrowerId, notification.BorrowerId);
    }

    [Fact]
    public async Task ReadyHold_DamagedThenRepaired_GetsAFreshCollectionPeriod_AndDoesNotExpireEarly()
    {
        using var db = TestDb.Create();
        var (central, _, _) = await Section1.SeedBranchesAsync(db);
        var book = await Section1.AddBookAsync(db, "B1", central);
        var holder = await AddBorrowerAsync(db, "Ready Holder");
        var ready = await AddHoldAsync(db, book, holder, ReservationStatus.Ready);
        var next = await AddHoldAsync(db, book, await AddBorrowerAsync(db, "Next In Line"));
        // The hold became Ready ten days ago, then the item was found damaged.
        ready.ReadyAt = DateTime.UtcNow.AddDays(-10);
        await db.SaveChangesAsync();
        var service = Reservations(db);

        await new ItemsController(db, null, service).Edit(BookForm(book, central, ItemStatus.Damaged));
        var expiredWhileDamaged = await service.ExpireUncollectedHoldsAsync();
        var beforeRepair = DateTime.UtcNow;
        await new ItemsController(db, null, service).Edit(BookForm(await db.Items.SingleAsync(), central, ItemStatus.Available));
        var expiredAfterRepair = await service.ExpireUncollectedHoldsAsync();

        Assert.Equal(0, expiredWhileDamaged);
        Assert.Equal(0, expiredAfterRepair);
        var hold = await db.Reservations.AsNoTracking().SingleAsync(r => r.Id == ready.Id);
        // Same borrower is first again, with a new ReadyAt, so the 3 days start now.
        Assert.Equal(ReservationStatus.Ready, hold.Status);
        Assert.True(hold.ReadyAt >= beforeRepair);
        Assert.Equal(ReservationStatus.Waiting, await StatusOfAsync(db, next));
        Assert.Equal(1, await db.Notifications.CountAsync(n => n.Type == NotificationType.ReservationReady && n.BorrowerId == holder.Id));
        Assert.Equal(0, await db.Notifications.CountAsync(n => n.Type == NotificationType.ReservationExpired));
    }

    [Fact]
    public async Task Expiry_ReadyHoldOnAnItemThatIsNotOnTheShelf_IsPutBackToWaitingInsteadOfExpiring()
    {
        using var db = TestDb.Create();
        var (central, _, _) = await Section1.SeedBranchesAsync(db);
        // A state older data could be in: Ready hold, old date, item Damaged.
        var book = await Section1.AddBookAsync(db, "B1", central, ItemStatus.Damaged);
        var ready = await AddHoldAsync(db, book, await AddBorrowerAsync(db, "Ready Holder"), ReservationStatus.Ready);
        ready.ReadyAt = DateTime.UtcNow.AddDays(-10);
        await db.SaveChangesAsync();

        var expired = await Reservations(db).ExpireUncollectedHoldsAsync();

        Assert.Equal(0, expired);
        var hold = await db.Reservations.AsNoTracking().SingleAsync();
        Assert.Equal(ReservationStatus.Waiting, hold.Status);
        Assert.Null(hold.ReadyAt);
    }

    [Fact]
    public async Task Expiry_StillExpiresAnUncollectedHoldOnAnAvailableItem()
    {
        using var db = TestDb.Create();
        var (central, _, _) = await Section1.SeedBranchesAsync(db);
        var book = await Section1.AddBookAsync(db, "B1", central);
        var ready = await AddHoldAsync(db, book, await AddBorrowerAsync(db, "Ready Holder"), ReservationStatus.Ready);
        var next = await AddHoldAsync(db, book, await AddBorrowerAsync(db, "Next In Line"));
        ready.ReadyAt = DateTime.UtcNow.AddDays(-(ReservationService.HoldDays + 1));
        await db.SaveChangesAsync();

        var expired = await Reservations(db).ExpireUncollectedHoldsAsync();

        Assert.Equal(1, expired);
        Assert.Equal(ReservationStatus.Expired, await StatusOfAsync(db, ready));
        Assert.Equal(ReservationStatus.Ready, await StatusOfAsync(db, next));
    }

    [Fact]
    public async Task AdminEdit_ThatKeepsTheItemAvailable_LeavesTheReadyHoldAlone()
    {
        using var db = TestDb.Create();
        var (central, _, _) = await Section1.SeedBranchesAsync(db);
        var book = await Section1.AddBookAsync(db, "B1", central);
        var ready = await AddHoldAsync(db, book, await AddBorrowerAsync(db, "Ready Holder"), ReservationStatus.Ready);
        var readyAt = (await db.Reservations.AsNoTracking().SingleAsync()).ReadyAt;

        await new ItemsController(db, null, Reservations(db)).Edit(BookForm(book, central, ItemStatus.Available));

        var hold = await db.Reservations.AsNoTracking().SingleAsync(r => r.Id == ready.Id);
        Assert.Equal(ReservationStatus.Ready, hold.Status);
        Assert.Equal(readyAt, hold.ReadyAt);
        Assert.Equal(0, await db.Notifications.CountAsync());
    }

    // ---------- The Reservations page shows where each item physically is

    [Fact]
    public async Task ReservationsIndex_LoadsEachItemsBranch_AndTheDesksOwnBranch()
    {
        using var db = TestDb.Create();
        var (central, north, _) = await Section1.SeedBranchesAsync(db);
        await Section1.AddStaffAsync(db, "reception", central);
        var here = await Section1.AddBookAsync(db, "B1", central, ItemStatus.Borrowed);
        var elsewhere = await Section1.AddBookAsync(db, "B2", north, ItemStatus.Borrowed);
        var borrower = await AddBorrowerAsync(db, "Wants Both");
        await AddHoldAsync(db, here, borrower);
        await AddHoldAsync(db, elsewhere, borrower);
        var controller = ReservationsController(db);

        var view = Assert.IsType<ViewResult>(await controller.Index());

        var holds = Assert.IsAssignableFrom<IEnumerable<Reservation>>(view.Model).ToList();
        Assert.Equal(new[] { "Central Library", "Northside Branch" }, holds.Select(h => h.Item.Branch!.Name).OrderBy(n => n));
        Assert.Equal(central.Id, (int?)controller.ViewBag.DeskBranchId);
    }

    // ---------- Issue 5: kiosk card look-ups and checkouts are rate limited

    [Theory]
    [InlineData(nameof(KioskController.Account))]
    [InlineData(nameof(KioskController.Checkout))]
    public void KioskPostActions_UseTheKioskRateLimitPolicy(string action)
    {
        var method = typeof(KioskController).GetMethods().Single(m => m.Name == action);

        var limit = method.GetCustomAttribute<EnableRateLimitingAttribute>();

        Assert.NotNull(limit);
        Assert.Equal(KioskOptions.PolicyName, limit!.PolicyName);
        Assert.NotNull(method.GetCustomAttribute<ValidateAntiForgeryTokenAttribute>());
    }

    // ---------- API: status must be a name

    [Theory]
    [InlineData("99")]
    [InlineData("1")]
    [InlineData("-1")]
    [InlineData("Destroy")]
    [InlineData("banana")]
    public async Task Api_UnknownOrNumericStatus_IsABadRequest(string status)
    {
        using var db = TestDb.Create();
        var controller = new CatalogueApiController(db, Options.Create(new PublicApiOptions()));

        var result = await controller.GetItems(status: status);

        var problem = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(400, problem.StatusCode);
    }

    [Theory]
    [InlineData("Borrowed")]
    [InlineData("intransit")]
    public async Task Api_NamedStatus_StillFilters(string status)
    {
        using var db = TestDb.Create();
        var (central, _, _) = await Section1.SeedBranchesAsync(db);
        await Section1.AddBookAsync(db, "B1", central, ItemStatus.Borrowed);
        await Section1.AddBookAsync(db, "B2", central, ItemStatus.InTransit);
        await Section1.AddBookAsync(db, "B3", central);
        var controller = new CatalogueApiController(db, Options.Create(new PublicApiOptions()));

        var result = await controller.GetItems(status: status);

        Assert.Equal(1, result.Value!.TotalItems);
    }

    // ---------- Issue 6: PDF export of a name the report font cannot draw

    [Fact]
    public async Task InventoryPdf_WithAnItemNameOutsideTheReportFont_StillRenders()
    {
        using var db = TestDb.Create();
        var (central, _, _) = await Section1.SeedBranchesAsync(db);
        await Section1.AddBookAsync(db, "B1", central, ItemStatus.Damaged, name: "図書館の本 📚 Café");

        var report = await new ReportService(db).BuildInventoryHealthReportAsync(new ReportFilter());
        var pdf = new ReportPdfService().Render(report);

        Assert.True(pdf.Length > 1000);
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(pdf, 0, 4));
    }

    [Fact]
    public void ReportFonts_StartWithTheReportFont_AndOnlyListInstalledFallbacks()
    {
        var families = ReportPdfService.ActiveFontFamilies;

        Assert.Equal("Lato", families[0]);
        Assert.Equal(families.Count, families.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        var installed = QuestPDF.Drawing.FontManager.GetSystemFonts().Select(f => f.FamilyName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.All(families.Skip(1), family => Assert.Contains(family, installed));
    }

    [Theory]
    [InlineData("図書館の本")]            // Japanese
    [InlineData("图书馆 도서관")]         // Chinese and Korean
    [InlineData("पुस्तकालय")]             // Devanagari (Hindi, Nepali)
    [InlineData("مكتبة ספרייה ห้องสมุด")] // Arabic, Hebrew, Thai
    [InlineData("Žluťoučký kůň Ελληνικά Кириллица")]
    public async Task EveryReportPdf_RendersItemNamesInOtherScripts(string name)
    {
        using var db = TestDb.Create();
        var (central, _, _) = await Section1.SeedBranchesAsync(db);
        var book = await Section1.AddBookAsync(db, "B1", central, name: name);
        var borrower = await AddBorrowerAsync(db, name);
        await LoansControllerFactory.Create(db).Borrow(new BorrowViewModel { LibraryCode = "B1", BorrowerId = borrower.Id });
        var service = new ReportService(db);
        var pdf = new ReportPdfService();

        var documents = new[]
        {
            pdf.Render(await service.BuildBorrowingReportAsync(new ReportFilter())),
            pdf.Render(await service.BuildFineAuditReportAsync(new ReportFilter())),
            pdf.Render(await service.BuildInventoryHealthReportAsync(new ReportFilter()))
        };

        Assert.All(documents, d => Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(d, 0, 4)));
    }
}
