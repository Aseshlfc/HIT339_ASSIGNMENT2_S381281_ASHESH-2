using LibraryManagementSystem.Controllers;
using LibraryManagementSystem.Data;
using LibraryManagementSystem.Models;
using LibraryManagementSystem.Services;
using LibraryManagementSystem.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Reflection;

namespace LibraryManagementSystem.Tests;

public class TransferServiceTests
{
    [Fact]
    public async Task Request_ValidTransfer_CreatesRequestedTransferAndLeavesItemAvailableAtSource()
    {
        using var db = TestDb.Create();
        var (central, north, _) = await Section1.SeedBranchesAsync(db);
        var book = await Section1.AddBookAsync(db, "B1", central);
        var service = new TransferService(db);

        var result = await service.RequestAsync(book.Id, north.Id, "  Requested by a borrower  ", "admin");

        Assert.True(result.Succeeded);
        var transfer = await db.ItemTransfers.SingleAsync();
        Assert.Equal(TransferStatus.Requested, transfer.Status);
        Assert.Equal(central.Id, transfer.FromBranchId);
        Assert.Equal(north.Id, transfer.ToBranchId);
        Assert.Equal("Requested by a borrower", transfer.Reason);
        Assert.Equal("admin", transfer.RequestedBy);
        Assert.Equal(ItemStatus.Available, book.Status);
        Assert.Equal(central.Id, book.BranchId);
    }

    [Fact]
    public async Task FullWorkflow_DispatchThenReceive_MovesItemToDestinationBranch()
    {
        using var db = TestDb.Create();
        var (central, north, _) = await Section1.SeedBranchesAsync(db);
        var book = await Section1.AddBookAsync(db, "B1", central);
        var service = new TransferService(db);
        var id = (await service.RequestAsync(book.Id, north.Id, null, "admin")).Transfer!.Id;

        var dispatched = await service.DispatchAsync(id, "desk-central");
        Assert.True(dispatched.Succeeded);
        Assert.Equal(ItemStatus.InTransit, book.Status);
        Assert.Equal(central.Id, book.BranchId); // still counted at the source while travelling

        var received = await service.ReceiveAsync(id, "desk-north");
        Assert.True(received.Succeeded);
        Assert.Equal(ItemStatus.Available, book.Status);
        Assert.Equal(north.Id, book.BranchId);

        var transfer = await db.ItemTransfers.SingleAsync();
        Assert.Equal(TransferStatus.Completed, transfer.Status);
        Assert.Equal("desk-central", transfer.DispatchedBy);
        Assert.Equal("desk-north", transfer.ClosedBy);
        Assert.NotNull(transfer.ClosedAt);
    }

    [Fact]
    public async Task Request_SameSourceAndDestination_IsRejected()
    {
        using var db = TestDb.Create();
        var (central, _, _) = await Section1.SeedBranchesAsync(db);
        var book = await Section1.AddBookAsync(db, "B1", central);

        var result = await new TransferService(db).RequestAsync(book.Id, central.Id, null, "admin");

        Assert.False(result.Succeeded);
        Assert.Contains("already held at", result.Message);
        Assert.Equal(0, await db.ItemTransfers.CountAsync());
    }

    [Theory]
    [InlineData(ItemStatus.Borrowed, "on loan")]
    [InlineData(ItemStatus.Damaged, "Damaged")]
    [InlineData(ItemStatus.Destroy, "Destroy")]
    [InlineData(ItemStatus.InTransit, "already in transit")]
    public async Task Request_ItemNotAvailable_IsRejected(ItemStatus status, string expectedText)
    {
        using var db = TestDb.Create();
        var (central, north, _) = await Section1.SeedBranchesAsync(db);
        var book = await Section1.AddBookAsync(db, "B1", central, status);

        var result = await new TransferService(db).RequestAsync(book.Id, north.Id, null, "admin");

        Assert.False(result.Succeeded);
        Assert.Contains(expectedText, result.Message);
        Assert.Equal(0, await db.ItemTransfers.CountAsync());
    }

    [Fact]
    public async Task Request_ItemMarkedAvailableButStillOnLoan_IsRejected()
    {
        // The baseline lets Admin set a borrowed item back to Available by
        // hand. A transfer must not build on that inconsistency.
        using var db = TestDb.Create();
        var (central, north, _) = await Section1.SeedBranchesAsync(db);
        var book = await Section1.AddBookAsync(db, "B1", central);
        var borrower = await Section1.AddBorrowerAsync(db);
        db.Loans.Add(new Loan { ItemId = book.Id, BorrowerId = borrower.Id, BorrowedAt = DateTime.Now, DueDate = DateTime.Now.AddDays(14) });
        await db.SaveChangesAsync();

        var result = await new TransferService(db).RequestAsync(book.Id, north.Id, null, "admin");

        Assert.False(result.Succeeded);
        Assert.Contains("open loan", result.Message);
    }

    [Fact]
    public async Task Request_DuplicateOpenTransfer_IsRejected()
    {
        using var db = TestDb.Create();
        var (central, north, river) = await Section1.SeedBranchesAsync(db);
        var book = await Section1.AddBookAsync(db, "B1", central);
        var service = new TransferService(db);
        await service.RequestAsync(book.Id, north.Id, null, "admin");

        var second = await service.RequestAsync(book.Id, river.Id, null, "admin");

        Assert.False(second.Succeeded);
        Assert.Contains("already has an open transfer", second.Message);
        Assert.Equal(1, await db.ItemTransfers.CountAsync());
    }

    [Fact]
    public async Task Request_MissingItemOrBranch_OrInactiveDestination_IsRejected()
    {
        using var db = TestDb.Create();
        var (central, north, _) = await Section1.SeedBranchesAsync(db);
        var book = await Section1.AddBookAsync(db, "B1", central);
        north.IsActive = false;
        await db.SaveChangesAsync();
        var service = new TransferService(db);

        Assert.False((await service.RequestAsync(9999, north.Id, null, "admin")).Succeeded);
        Assert.False((await service.RequestAsync(book.Id, 9999, null, "admin")).Succeeded);
        var inactive = await service.RequestAsync(book.Id, north.Id, null, "admin");
        Assert.False(inactive.Succeeded);
        Assert.Contains("inactive", inactive.Message);
    }

    [Fact]
    public async Task Dispatch_ItemBorrowedAfterRequest_FailsAndLeavesLoanUntouched()
    {
        using var db = TestDb.Create();
        var (central, north, _) = await Section1.SeedBranchesAsync(db);
        var book = await Section1.AddBookAsync(db, "B1", central);
        var borrower = await Section1.AddBorrowerAsync(db);
        var service = new TransferService(db);
        var id = (await service.RequestAsync(book.Id, north.Id, null, "admin")).Transfer!.Id;

        // The item is still on the shelf while only requested, so it can be borrowed.
        await new LoansController(db).Borrow(new BorrowViewModel { LibraryCode = "B1", BorrowerId = borrower.Id });
        var result = await service.DispatchAsync(id, "admin");

        Assert.False(result.Succeeded);
        Assert.Equal(ItemStatus.Borrowed, book.Status);
        Assert.Equal(central.Id, book.BranchId);
        var loan = await db.Loans.SingleAsync();
        Assert.Null(loan.ReturnedAt);
        Assert.Equal(TransferStatus.Requested, (await db.ItemTransfers.SingleAsync()).Status);
    }

    [Fact]
    public async Task InTransitItem_CannotBeBorrowed()
    {
        using var db = TestDb.Create();
        var (central, north, _) = await Section1.SeedBranchesAsync(db);
        var book = await Section1.AddBookAsync(db, "B1", central);
        var borrower = await Section1.AddBorrowerAsync(db);
        var service = new TransferService(db);
        var id = (await service.RequestAsync(book.Id, north.Id, null, "admin")).Transfer!.Id;
        await service.DispatchAsync(id, "admin");

        var controller = new LoansController(db);
        await controller.Borrow(new BorrowViewModel { LibraryCode = "B1", BorrowerId = borrower.Id });

        Assert.False(controller.ModelState.IsValid);
        Assert.Equal(0, await db.Loans.CountAsync());
    }

    [Fact]
    public async Task Receive_CalledTwice_SecondCallFailsAndBranchIsUnchanged()
    {
        using var db = TestDb.Create();
        var (central, north, _) = await Section1.SeedBranchesAsync(db);
        var book = await Section1.AddBookAsync(db, "B1", central);
        var service = new TransferService(db);
        var id = (await service.RequestAsync(book.Id, north.Id, null, "admin")).Transfer!.Id;
        await service.DispatchAsync(id, "admin");
        await service.ReceiveAsync(id, "admin");

        var second = await service.ReceiveAsync(id, "admin");

        Assert.False(second.Succeeded);
        Assert.Contains("already been completed", second.Message);
        Assert.Equal(north.Id, book.BranchId);
        Assert.Equal(ItemStatus.Available, book.Status);
    }

    [Fact]
    public async Task Dispatch_CalledTwice_SecondCallFails()
    {
        using var db = TestDb.Create();
        var (central, north, _) = await Section1.SeedBranchesAsync(db);
        var book = await Section1.AddBookAsync(db, "B1", central);
        var service = new TransferService(db);
        var id = (await service.RequestAsync(book.Id, north.Id, null, "admin")).Transfer!.Id;
        await service.DispatchAsync(id, "first");

        var second = await service.DispatchAsync(id, "second");

        Assert.False(second.Succeeded);
        Assert.Equal("first", (await db.ItemTransfers.SingleAsync()).DispatchedBy);
        Assert.Equal(ItemStatus.InTransit, book.Status);
    }

    [Fact]
    public async Task UnknownTransferId_EveryActionFailsWithoutThrowing()
    {
        using var db = TestDb.Create();
        var service = new TransferService(db);

        Assert.False((await service.DispatchAsync(404, "a")).Succeeded);
        Assert.False((await service.ReceiveAsync(404, "a")).Succeeded);
        Assert.False((await service.CancelAsync(404, "a")).Succeeded);
        Assert.False((await service.RejectAsync(404, "a")).Succeeded);
    }

    [Fact]
    public async Task Receive_BeforeDispatch_IsRejected()
    {
        using var db = TestDb.Create();
        var (central, north, _) = await Section1.SeedBranchesAsync(db);
        var book = await Section1.AddBookAsync(db, "B1", central);
        var service = new TransferService(db);
        var id = (await service.RequestAsync(book.Id, north.Id, null, "admin")).Transfer!.Id;

        var result = await service.ReceiveAsync(id, "admin");

        Assert.False(result.Succeeded);
        Assert.Equal(central.Id, book.BranchId);
    }

    [Fact]
    public async Task Cancel_RequestedTransfer_ClosesItAndItemStaysPut()
    {
        using var db = TestDb.Create();
        var (central, north, _) = await Section1.SeedBranchesAsync(db);
        var book = await Section1.AddBookAsync(db, "B1", central);
        var service = new TransferService(db);
        var id = (await service.RequestAsync(book.Id, north.Id, null, "admin")).Transfer!.Id;

        var result = await service.CancelAsync(id, "admin", "No longer needed");

        Assert.True(result.Succeeded);
        var transfer = await db.ItemTransfers.SingleAsync();
        Assert.Equal(TransferStatus.Cancelled, transfer.Status);
        Assert.Equal("No longer needed", transfer.ClosureNote);
        Assert.Equal(ItemStatus.Available, book.Status);
        Assert.Equal(central.Id, book.BranchId);
    }

    [Fact]
    public async Task Cancel_InTransitTransfer_ReturnsItemToSourceAsAvailable()
    {
        using var db = TestDb.Create();
        var (central, north, _) = await Section1.SeedBranchesAsync(db);
        var book = await Section1.AddBookAsync(db, "B1", central);
        var service = new TransferService(db);
        var id = (await service.RequestAsync(book.Id, north.Id, null, "admin")).Transfer!.Id;
        await service.DispatchAsync(id, "admin");

        var result = await service.CancelAsync(id, "admin");

        Assert.True(result.Succeeded);
        Assert.Equal(ItemStatus.Available, book.Status);
        Assert.Equal(central.Id, book.BranchId);
    }

    [Fact]
    public async Task Cancel_CompletedTransfer_IsRejected_AndRejectOnlyWorksWhileRequested()
    {
        using var db = TestDb.Create();
        var (central, north, _) = await Section1.SeedBranchesAsync(db);
        var book = await Section1.AddBookAsync(db, "B1", central);
        var service = new TransferService(db);
        var id = (await service.RequestAsync(book.Id, north.Id, null, "admin")).Transfer!.Id;
        await service.DispatchAsync(id, "admin");

        Assert.False((await service.RejectAsync(id, "admin")).Succeeded);
        await service.ReceiveAsync(id, "admin");
        Assert.False((await service.CancelAsync(id, "admin")).Succeeded);
        Assert.Equal(TransferStatus.Completed, (await db.ItemTransfers.SingleAsync()).Status);
    }

    [Fact]
    public async Task Reject_RequestedTransfer_ClosesItAndAllowsANewRequest()
    {
        using var db = TestDb.Create();
        var (central, north, _) = await Section1.SeedBranchesAsync(db);
        var book = await Section1.AddBookAsync(db, "B1", central);
        var service = new TransferService(db);
        var id = (await service.RequestAsync(book.Id, north.Id, null, "admin")).Transfer!.Id;

        var rejected = await service.RejectAsync(id, "desk-central", "Reserved for a display");
        var again = await service.RequestAsync(book.Id, north.Id, null, "admin");

        Assert.True(rejected.Succeeded);
        Assert.True(again.Succeeded);
        Assert.Equal(2, await db.ItemTransfers.CountAsync());
    }

    [Fact]
    public async Task CompletedTransfer_PreservesExistingLoanHistory()
    {
        using var db = TestDb.Create();
        var (central, north, _) = await Section1.SeedBranchesAsync(db);
        var book = await Section1.AddBookAsync(db, "B1", central);
        var borrower = await Section1.AddBorrowerAsync(db);
        db.Loans.Add(new Loan
        {
            ItemId = book.Id, BorrowerId = borrower.Id, BranchId = central.Id,
            BorrowedAt = DateTime.Now.AddDays(-30), DueDate = DateTime.Now.AddDays(-16), ReturnedAt = DateTime.Now.AddDays(-12),
            FineAmount = 4.00m, FinePaid = true
        });
        await db.SaveChangesAsync();
        var service = new TransferService(db);

        var id = (await service.RequestAsync(book.Id, north.Id, null, "admin")).Transfer!.Id;
        await service.DispatchAsync(id, "admin");
        await service.ReceiveAsync(id, "admin");

        var loan = await db.Loans.SingleAsync();
        Assert.Equal(central.Id, loan.BranchId); // issuing branch is history, it does not follow the item
        Assert.Equal(4.00m, loan.FineAmount);
        Assert.True(loan.FinePaid);
        Assert.NotNull(loan.ReturnedAt);
    }

    [Fact]
    public async Task RealSqlite_TwoDesksReceivingTheSameTransfer_OnlyOneSucceeds()
    {
        using var sqlite = new SqliteTestDb();
        int transferId, northId;
        using (var seed = sqlite.Open())
        {
            var (central, north, _) = await Section1.SeedBranchesAsync(seed);
            var book = await Section1.AddBookAsync(seed, "B1", central);
            var service = new TransferService(seed);
            transferId = (await service.RequestAsync(book.Id, north.Id, null, "admin")).Transfer!.Id;
            await service.DispatchAsync(transferId, "admin");
            northId = north.Id;
        }

        using var requestA = sqlite.Open();
        using var requestB = sqlite.Open();
        // Both requests load the transfer while it is still in transit.
        _ = await requestA.ItemTransfers.Include(t => t.Item).SingleAsync(t => t.Id == transferId);
        _ = await requestB.ItemTransfers.Include(t => t.Item).SingleAsync(t => t.Id == transferId);

        var first = await new TransferService(requestA).ReceiveAsync(transferId, "desk-a");
        var second = await new TransferService(requestB).ReceiveAsync(transferId, "desk-b");

        Assert.True(first.Succeeded);
        Assert.False(second.Succeeded);
        using var verify = sqlite.Open();
        var transfer = await verify.ItemTransfers.Include(t => t.Item).SingleAsync();
        Assert.Equal(TransferStatus.Completed, transfer.Status);
        Assert.Equal("desk-a", transfer.ClosedBy);
        Assert.Equal(northId, transfer.Item.BranchId);
    }

    [Fact]
    public async Task RealSqlite_BorrowRacingWithDispatch_DoesNotSendABorrowedItemAway()
    {
        using var sqlite = new SqliteTestDb();
        int transferId, borrowerId;
        using (var seed = sqlite.Open())
        {
            var (central, north, _) = await Section1.SeedBranchesAsync(seed);
            var book = await Section1.AddBookAsync(seed, "B1", central);
            borrowerId = (await Section1.AddBorrowerAsync(seed)).Id;
            transferId = (await new TransferService(seed).RequestAsync(book.Id, north.Id, null, "admin")).Transfer!.Id;
        }

        using var deskDb = sqlite.Open();
        using var dispatchDb = sqlite.Open();
        // The dispatching request reads the item as Available...
        _ = await dispatchDb.ItemTransfers.Include(t => t.Item).SingleAsync(t => t.Id == transferId);
        // ...then a borrow at the desk wins the race...
        await new LoansController(deskDb).Borrow(new BorrowViewModel { LibraryCode = "B1", BorrowerId = borrowerId });
        // ...so the dispatch must fail rather than overwrite Borrowed with InTransit.
        var result = await new TransferService(dispatchDb).DispatchAsync(transferId, "admin");

        Assert.False(result.Succeeded);
        using var verify = sqlite.Open();
        Assert.Equal(ItemStatus.Borrowed, (await verify.Items.SingleAsync()).Status);
        Assert.Equal(TransferStatus.Requested, (await verify.ItemTransfers.SingleAsync()).Status);
        Assert.Null((await verify.Loans.SingleAsync()).ReturnedAt);
    }

    [Fact]
    public async Task RealSqlite_DatabaseRejectsASecondOpenTransferForTheSameItem()
    {
        using var sqlite = new SqliteTestDb();
        using var db = sqlite.Open();
        var (central, north, river) = await Section1.SeedBranchesAsync(db);
        var book = await Section1.AddBookAsync(db, "B1", central);
        db.ItemTransfers.Add(new ItemTransfer { ItemId = book.Id, FromBranchId = central.Id, ToBranchId = north.Id, RequestedBy = "a", RequestedAt = DateTime.Now });
        await db.SaveChangesAsync();

        // Bypasses the service's own check to prove the unique index is the backstop.
        db.ItemTransfers.Add(new ItemTransfer { ItemId = book.Id, FromBranchId = central.Id, ToBranchId = river.Id, RequestedBy = "b", RequestedAt = DateTime.Now });

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }
}

public class TransfersControllerTests
{
    private static TransfersController Controller(LibraryContext db, string user, string role)
    {
        var controller = new TransfersController(db, new TransferService(db));
        Section1.SignIn(controller, user, role);
        return controller;
    }

    private static async Task<(Branch central, Branch north, Branch river, Book book)> SeedAsync(LibraryContext db)
    {
        var (central, north, river) = await Section1.SeedBranchesAsync(db);
        var book = await Section1.AddBookAsync(db, "B1", central);
        await Section1.AddStaffAsync(db, "desk-central", central);
        await Section1.AddStaffAsync(db, "desk-north", north);
        await Section1.AddStaffAsync(db, "desk-river", river);
        await Section1.AddStaffAsync(db, "desk-none", null);
        return (central, north, river, book);
    }

    [Fact]
    public void Transfers_AreRestrictedToAdminAndReception()
    {
        var roles = typeof(TransfersController).GetCustomAttribute<AuthorizeAttribute>()!.Roles;
        Assert.Equal("Admin,Reception", roles);
    }

    [Fact]
    public async Task Create_ReceptionRequestingItemToOwnBranch_Succeeds()
    {
        using var db = TestDb.Create();
        var (_, north, _, book) = await SeedAsync(db);
        var controller = Controller(db, "desk-north", "Reception");

        var result = await controller.Create(new TransferCreateViewModel { ItemId = book.Id, ToBranchId = north.Id });

        Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("desk-north", (await db.ItemTransfers.SingleAsync()).RequestedBy);
    }

    [Fact]
    public async Task Create_ReceptionMovingStockBetweenTwoOtherBranches_IsRefused()
    {
        using var db = TestDb.Create();
        var (_, north, _, book) = await SeedAsync(db);
        var controller = Controller(db, "desk-river", "Reception");

        var result = await controller.Create(new TransferCreateViewModel { ItemId = book.Id, ToBranchId = north.Id });

        Assert.IsType<ViewResult>(result);
        Assert.False(controller.ModelState.IsValid);
        Assert.Equal(0, await db.ItemTransfers.CountAsync());
    }

    [Fact]
    public async Task Create_ReceptionWithoutBranch_IsRefused()
    {
        using var db = TestDb.Create();
        var (_, north, _, book) = await SeedAsync(db);
        var controller = Controller(db, "desk-none", "Reception");

        var result = await controller.Create(new TransferCreateViewModel { ItemId = book.Id, ToBranchId = north.Id });

        Assert.IsType<RedirectToActionResult>(result);
        Assert.Contains("not assigned to a branch", (string)controller.TempData["Error"]!);
        Assert.Equal(0, await db.ItemTransfers.CountAsync());
    }

    [Fact]
    public async Task Create_ServiceFailure_IsShownAsValidationError()
    {
        using var db = TestDb.Create();
        var (central, _, _, book) = await SeedAsync(db);
        var controller = Controller(db, "admin", "Admin");

        var result = await controller.Create(new TransferCreateViewModel { ItemId = book.Id, ToBranchId = central.Id });

        Assert.IsType<ViewResult>(result);
        Assert.False(controller.ModelState.IsValid);
    }

    [Fact]
    public async Task Dispatch_OnlySourceBranchDeskMayDispatch()
    {
        using var db = TestDb.Create();
        var (_, north, _, book) = await SeedAsync(db);
        var id = (await new TransferService(db).RequestAsync(book.Id, north.Id, null, "admin")).Transfer!.Id;

        var destinationDesk = Controller(db, "desk-north", "Reception");
        await destinationDesk.Dispatch(id);
        Assert.Equal(TransferStatus.Requested, (await db.ItemTransfers.SingleAsync()).Status);
        Assert.Contains("source branch", (string)destinationDesk.TempData["Error"]!);

        var sourceDesk = Controller(db, "desk-central", "Reception");
        await sourceDesk.Dispatch(id);
        Assert.Equal(TransferStatus.InTransit, (await db.ItemTransfers.SingleAsync()).Status);
    }

    [Fact]
    public async Task Receive_OnlyDestinationBranchDeskMayReceive()
    {
        using var db = TestDb.Create();
        var (_, north, _, book) = await SeedAsync(db);
        var service = new TransferService(db);
        var id = (await service.RequestAsync(book.Id, north.Id, null, "admin")).Transfer!.Id;
        await service.DispatchAsync(id, "admin");

        await Controller(db, "desk-central", "Reception").Receive(id);
        Assert.Equal(TransferStatus.InTransit, (await db.ItemTransfers.SingleAsync()).Status);

        await Controller(db, "desk-north", "Reception").Receive(id);
        Assert.Equal(TransferStatus.Completed, (await db.ItemTransfers.SingleAsync()).Status);
        Assert.Equal(north.Id, book.BranchId);
    }

    [Fact]
    public async Task UninvolvedBranchDesk_CannotCancel_ButAdminCan()
    {
        using var db = TestDb.Create();
        var (_, north, _, book) = await SeedAsync(db);
        var id = (await new TransferService(db).RequestAsync(book.Id, north.Id, null, "admin")).Transfer!.Id;

        await Controller(db, "desk-river", "Reception").Cancel(id);
        Assert.Equal(TransferStatus.Requested, (await db.ItemTransfers.SingleAsync()).Status);

        await Controller(db, "admin", "Admin").Cancel(id, "Raised in error");
        Assert.Equal(TransferStatus.Cancelled, (await db.ItemTransfers.SingleAsync()).Status);
    }

    [Fact]
    public async Task UnknownTransferId_ReturnsNotFound()
    {
        using var db = TestDb.Create();
        await SeedAsync(db);
        var controller = Controller(db, "admin", "Admin");

        Assert.IsType<NotFoundResult>(await controller.Details(404));
        Assert.IsType<NotFoundResult>(await controller.Dispatch(404));
        Assert.IsType<NotFoundResult>(await controller.Receive(404));
        Assert.IsType<NotFoundResult>(await controller.Cancel(404));
        Assert.IsType<NotFoundResult>(await controller.Reject(404));
    }

    [Fact]
    public async Task Create_MissingItemOrDestination_IsAValidationError()
    {
        using var db = TestDb.Create();
        await SeedAsync(db);
        var controller = Controller(db, "admin", "Admin");

        var result = await controller.Create(new TransferCreateViewModel { ItemId = 9999, ToBranchId = 9999 });

        Assert.IsType<ViewResult>(result);
        Assert.False(controller.ModelState.IsValid);
        Assert.Equal(0, await db.ItemTransfers.CountAsync());
    }

    [Fact]
    public async Task Index_OpenViewAndHistoryView_SplitTransfersByStatus()
    {
        using var db = TestDb.Create();
        var (_, north, river, book) = await SeedAsync(db);
        var other = await Section1.AddBookAsync(db, "B2", river);
        var service = new TransferService(db);
        var done = (await service.RequestAsync(book.Id, north.Id, null, "admin")).Transfer!.Id;
        await service.CancelAsync(done, "admin");
        await service.RequestAsync(other.Id, north.Id, null, "admin");
        var controller = Controller(db, "admin", "Admin");

        var open = (TransferIndexViewModel)((ViewResult)await controller.Index("open")).Model!;
        var history = (TransferIndexViewModel)((ViewResult)await controller.Index("history")).Model!;
        var all = (TransferIndexViewModel)((ViewResult)await controller.Index("all")).Model!;

        Assert.Equal("B2", Assert.Single(open.Rows).Transfer.Item.LibraryCode);
        Assert.Equal("B1", Assert.Single(history.Rows).Transfer.Item.LibraryCode);
        Assert.Equal(2, all.Rows.Count);
    }
}
