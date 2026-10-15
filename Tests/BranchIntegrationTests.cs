using LibraryManagementSystem.Controllers;
using LibraryManagementSystem.Models;
using LibraryManagementSystem.Services;
using LibraryManagementSystem.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LibraryManagementSystem.Tests;

/// <summary>How branches plug into the existing item, loan, staff and search features.</summary>
public class BranchIntegrationTests
{
    [Fact]
    public async Task ItemCreate_WithBranch_AssignsItemToThatBranch()
    {
        using var db = TestDb.Create();
        var (_, north, _) = await Section1.SeedBranchesAsync(db);
        var controller = new ItemsController(db);

        await controller.Create(new ItemFormViewModel { ItemType = "Music", Name = "Song", Description = "D", LibraryCode = "M1", Artist = "A", BranchId = north.Id });

        Assert.Equal(north.Id, (await db.Items.SingleAsync()).BranchId);
    }

    [Fact]
    public async Task ItemCreate_WithoutBranch_FallsBackToDefaultBranch()
    {
        using var db = TestDb.Create();
        var controller = new ItemsController(db);

        await controller.Create(new ItemFormViewModel { ItemType = "Music", Name = "Song", Description = "D", LibraryCode = "M1", Artist = "A" });

        Assert.Equal(Branch.DefaultBranchId, (await db.Items.SingleAsync()).BranchId);
    }

    [Fact]
    public async Task ItemCreate_UnknownOrInactiveBranch_IsRejected()
    {
        using var db = TestDb.Create();
        var (_, north, _) = await Section1.SeedBranchesAsync(db);
        north.IsActive = false;
        await db.SaveChangesAsync();

        var unknown = new ItemsController(db);
        await unknown.Create(new ItemFormViewModel { ItemType = "Music", Name = "S", Description = "D", LibraryCode = "M1", Artist = "A", BranchId = 9999 });
        var inactive = new ItemsController(db);
        await inactive.Create(new ItemFormViewModel { ItemType = "Music", Name = "S", Description = "D", LibraryCode = "M2", Artist = "A", BranchId = north.Id });

        Assert.True(unknown.ModelState.ContainsKey(nameof(ItemFormViewModel.BranchId)));
        Assert.True(inactive.ModelState.ContainsKey(nameof(ItemFormViewModel.BranchId)));
        Assert.Equal(0, await db.Items.CountAsync());
    }

    [Fact]
    public async Task ItemIndex_FiltersByBranch()
    {
        using var db = TestDb.Create();
        var (central, north, _) = await Section1.SeedBranchesAsync(db);
        await Section1.AddBookAsync(db, "B1", central);
        await Section1.AddBookAsync(db, "B2", north);
        await Section1.AddBookAsync(db, "B3", north);
        var controller = new ItemsController(db);

        var all = (IEnumerable<Item>)((ViewResult)await controller.Index()).Model!;
        var northOnly = (IEnumerable<Item>)((ViewResult)await controller.Index(north.Id)).Model!;

        Assert.Equal(3, all.Count());
        Assert.Equal(new[] { "B2", "B3" }, northOnly.Select(i => i.LibraryCode));
    }

    [Fact]
    public async Task ItemEdit_CannotChangeBranchDirectly_SoNoMoveEscapesTheHistory()
    {
        using var db = TestDb.Create();
        var (central, _, river) = await Section1.SeedBranchesAsync(db);
        var book = await Section1.AddBookAsync(db, "B1", central);
        var controller = new ItemsController(db);

        var result = await controller.Edit(new ItemFormViewModel
        {
            Id = book.Id, ItemType = "Book", Name = "Renamed", Description = "d", LibraryCode = "B1",
            Author = "A", BookGenre = "G", Status = ItemStatus.Available, BranchId = river.Id
        });

        Assert.IsType<ViewResult>(result);
        Assert.True(controller.ModelState.ContainsKey(nameof(ItemFormViewModel.BranchId)));
        Assert.Equal(central.Id, book.BranchId);
        Assert.Equal(0, await db.ItemTransfers.CountAsync());
    }

    [Fact]
    public async Task ItemEdit_OtherFieldsStillSave_AndBranchIsKept()
    {
        using var db = TestDb.Create();
        var (_, north, _) = await Section1.SeedBranchesAsync(db);
        var book = await Section1.AddBookAsync(db, "B1", north);
        var controller = new ItemsController(db);

        var result = await controller.Edit(new ItemFormViewModel
        {
            Id = book.Id, ItemType = "Book", Name = "Renamed", Description = "new", LibraryCode = "B1",
            Author = "A2", BookGenre = "G2", Status = ItemStatus.Damaged, BranchId = north.Id
        });

        Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Renamed", book.Name);
        Assert.Equal("A2", book.Author);
        Assert.Equal(ItemStatus.Damaged, book.Status);
        Assert.Equal(north.Id, book.BranchId);
    }

    [Fact]
    public async Task ItemEdit_StatusCannotChangeWhileATransferIsOpen()
    {
        using var db = TestDb.Create();
        var (central, north, _) = await Section1.SeedBranchesAsync(db);
        var book = await Section1.AddBookAsync(db, "B1", central);
        await new TransferService(db).RequestAsync(book.Id, north.Id, null, "admin");
        var controller = new ItemsController(db);

        await controller.Edit(new ItemFormViewModel
        {
            Id = book.Id, ItemType = "Book", Name = "n", Description = "d", LibraryCode = "B1",
            Author = "A", BookGenre = "G", Status = ItemStatus.Damaged, BranchId = central.Id
        });

        Assert.False(controller.ModelState.IsValid);
        Assert.Equal(ItemStatus.Available, book.Status);
    }

    [Fact]
    public async Task CorrectBranch_MovesItemAndRecordsAnAuditedCorrection()
    {
        using var db = TestDb.Create();
        var (central, north, _) = await Section1.SeedBranchesAsync(db);
        var book = await Section1.AddBookAsync(db, "B1", central, ItemStatus.Damaged);
        var controller = new ItemsController(db);
        Section1.SignIn(controller, "admin", "Admin");

        var result = await controller.CorrectBranch(new ItemBranchCorrectionViewModel { ItemId = book.Id, ToBranchId = north.Id, Reason = " Catalogued against the wrong branch " });

        Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(north.Id, book.BranchId);
        Assert.Equal(ItemStatus.Damaged, book.Status); // a correction never changes status
        var record = await db.ItemTransfers.SingleAsync();
        Assert.Equal(TransferKind.Correction, record.Kind);
        Assert.Equal(TransferStatus.Completed, record.Status);
        Assert.Equal(central.Id, record.FromBranchId);
        Assert.Equal(north.Id, record.ToBranchId);
        Assert.Equal("Catalogued against the wrong branch", record.Reason);
        Assert.Equal("admin", record.RequestedBy);
    }

    [Fact]
    public async Task CorrectBranch_RequiresAReason_AndADifferentActiveBranch()
    {
        using var db = TestDb.Create();
        var (central, north, river) = await Section1.SeedBranchesAsync(db);
        river.IsActive = false;
        var book = await Section1.AddBookAsync(db, "B1", central);
        var service = new TransferService(db);

        Assert.False((await service.RecordCorrectionAsync(book.Id, north.Id, "  ", "admin")).Succeeded);
        Assert.False((await service.RecordCorrectionAsync(book.Id, central.Id, "why", "admin")).Succeeded);
        Assert.False((await service.RecordCorrectionAsync(book.Id, river.Id, "why", "admin")).Succeeded);
        Assert.False((await service.RecordCorrectionAsync(book.Id, 9999, "why", "admin")).Succeeded);
        Assert.False((await service.RecordCorrectionAsync(9999, north.Id, "why", "admin")).Succeeded);

        Assert.Equal(central.Id, book.BranchId);
        Assert.Equal(0, await db.ItemTransfers.CountAsync());
    }

    [Fact]
    public async Task CorrectBranch_IsRefusedWhileItemIsOnLoanInTransitOrBeingTransferred()
    {
        using var db = TestDb.Create();
        var (central, north, river) = await Section1.SeedBranchesAsync(db);
        var onLoan = await Section1.AddBookAsync(db, "B1", central, ItemStatus.Borrowed);
        var requested = await Section1.AddBookAsync(db, "B2", central);
        var inTransit = await Section1.AddBookAsync(db, "B3", central);
        var service = new TransferService(db);
        await service.RequestAsync(requested.Id, north.Id, null, "admin");
        var id = (await service.RequestAsync(inTransit.Id, north.Id, null, "admin")).Transfer!.Id;
        await service.DispatchAsync(id, "admin");

        Assert.Contains("on loan", (await service.RecordCorrectionAsync(onLoan.Id, river.Id, "why", "admin")).Message);
        Assert.Contains("open transfer", (await service.RecordCorrectionAsync(requested.Id, river.Id, "why", "admin")).Message);
        Assert.Contains("open transfer", (await service.RecordCorrectionAsync(inTransit.Id, river.Id, "why", "admin")).Message);

        Assert.All(new[] { onLoan, requested, inTransit }, b => Assert.Equal(central.Id, b.BranchId));
        Assert.Equal(0, await db.ItemTransfers.CountAsync(t => t.Kind == TransferKind.Correction));
    }

    [Fact]
    public async Task ItemEdit_CannotSetInTransitByHand()
    {
        using var db = TestDb.Create();
        var (central, _, _) = await Section1.SeedBranchesAsync(db);
        var book = await Section1.AddBookAsync(db, "B1", central);
        var controller = new ItemsController(db);

        await controller.Edit(new ItemFormViewModel
        {
            Id = book.Id, ItemType = "Book", Name = "n", Description = "d", LibraryCode = "B1",
            Author = "A", BookGenre = "G", Status = ItemStatus.InTransit, BranchId = central.Id
        });

        Assert.False(controller.ModelState.IsValid);
        Assert.Equal(ItemStatus.Available, book.Status);
    }

    [Fact]
    public async Task Borrow_RecordsTheBranchHoldingTheItemAsIssuingBranch()
    {
        using var db = TestDb.Create();
        var (_, north, _) = await Section1.SeedBranchesAsync(db);
        await Section1.AddBookAsync(db, "B1", north);
        var borrower = await Section1.AddBorrowerAsync(db);

        await LoansControllerFactory.Create(db).Borrow(new BorrowViewModel { LibraryCode = "B1", BorrowerId = borrower.Id });

        Assert.Equal(north.Id, (await db.Loans.SingleAsync()).BranchId);
    }

    [Fact]
    public async Task Borrow_ReceptionDeskCannotLendAnItemHeldAtAnotherBranch()
    {
        using var db = TestDb.Create();
        var (central, north, _) = await Section1.SeedBranchesAsync(db);
        await Section1.AddBookAsync(db, "B1", north);
        var borrower = await Section1.AddBorrowerAsync(db);
        await Section1.AddStaffAsync(db, "desk-central", central);
        var controller = LoansControllerFactory.Create(db);
        Section1.SignIn(controller, "desk-central", "Reception");

        var result = await controller.Borrow(new BorrowViewModel { LibraryCode = "B1", BorrowerId = borrower.Id });

        Assert.IsType<ViewResult>(result);
        Assert.Contains("Northside Branch", controller.ModelState[nameof(BorrowViewModel.LibraryCode)]!.Errors[0].ErrorMessage);
        Assert.Equal(0, await db.Loans.CountAsync());
    }

    [Fact]
    public async Task Borrow_ReceptionDeskCanLendItsOwnBranchsItem()
    {
        using var db = TestDb.Create();
        var (_, north, _) = await Section1.SeedBranchesAsync(db);
        await Section1.AddBookAsync(db, "B1", north);
        var borrower = await Section1.AddBorrowerAsync(db);
        await Section1.AddStaffAsync(db, "desk-north", north);
        var controller = LoansControllerFactory.Create(db);
        Section1.SignIn(controller, "desk-north", "Reception");

        var result = await controller.Borrow(new BorrowViewModel { LibraryCode = "B1", BorrowerId = borrower.Id });

        Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(1, await db.Loans.CountAsync());
    }

    [Fact]
    public async Task Return_CannotLeaveItemInTransit()
    {
        using var db = TestDb.Create();
        var (central, _, _) = await Section1.SeedBranchesAsync(db);
        var book = await Section1.AddBookAsync(db, "B1", central, ItemStatus.Borrowed);
        var borrower = await Section1.AddBorrowerAsync(db);
        var loan = new Loan { ItemId = book.Id, BorrowerId = borrower.Id, BorrowedAt = DateTime.Now, DueDate = DateTime.Now.AddDays(14) };
        db.Loans.Add(loan);
        await db.SaveChangesAsync();
        var controller = LoansControllerFactory.Create(db);

        await controller.Return(new ReturnViewModel { LoanId = loan.Id, ReturnStatus = ItemStatus.InTransit });

        Assert.False(controller.ModelState.IsValid);
        Assert.Null(loan.ReturnedAt);
    }

    [Fact]
    public async Task Borrow_ReceptionWithoutABranch_CannotLendAnything()
    {
        using var db = TestDb.Create();
        var (central, _, _) = await Section1.SeedBranchesAsync(db);
        var book = await Section1.AddBookAsync(db, "B1", central);
        var borrower = await Section1.AddBorrowerAsync(db);
        await Section1.AddStaffAsync(db, "desk-none", null);
        var controller = LoansControllerFactory.Create(db);
        Section1.SignIn(controller, "desk-none", "Reception");

        var get = await controller.Borrow("B1");
        var post = await controller.Borrow(new BorrowViewModel { LibraryCode = "B1", BorrowerId = borrower.Id });

        Assert.IsType<RedirectToActionResult>(get);
        Assert.IsType<RedirectToActionResult>(post);
        Assert.Contains("not assigned to a branch", (string)controller.TempData["Error"]!);
        Assert.Equal(0, await db.Loans.CountAsync());
        Assert.Equal(ItemStatus.Available, book.Status);
    }

    [Fact]
    public async Task Return_ReceptionWithoutABranch_CannotAcceptReturns()
    {
        using var db = TestDb.Create();
        var (central, _, _) = await Section1.SeedBranchesAsync(db);
        var (loan, book) = await AddOpenLoanAsync(db, central);
        await Section1.AddStaffAsync(db, "desk-none", null);
        var controller = LoansControllerFactory.Create(db);
        Section1.SignIn(controller, "desk-none", "Reception");

        await controller.Return(loan.Id);
        await controller.Return(new ReturnViewModel { LoanId = loan.Id, ReturnStatus = ItemStatus.Available });

        Assert.Null(loan.ReturnedAt);
        Assert.Equal(ItemStatus.Borrowed, book.Status);
    }

    private static async Task<(Loan loan, Book book)> AddOpenLoanAsync(Data.LibraryContext db, Branch branch, int daysOverdue = 0)
    {
        var book = await Section1.AddBookAsync(db, $"L{Guid.NewGuid():N}"[..8], branch, ItemStatus.Borrowed);
        var borrower = await Section1.AddBorrowerAsync(db);
        var due = DateTime.Now.Date.AddDays(daysOverdue > 0 ? -daysOverdue : 7);
        var loan = new Loan { ItemId = book.Id, BorrowerId = borrower.Id, BranchId = branch.Id, BorrowedAt = due.AddDays(-LibraryRules.LoanDays), DueDate = due };
        db.Loans.Add(loan);
        await db.SaveChangesAsync();
        return (loan, book);
    }

    [Fact]
    public async Task Return_AtTheHoldingBranch_LeavesLocationAloneAndRecordsNoMovement()
    {
        using var db = TestDb.Create();
        var (central, _, _) = await Section1.SeedBranchesAsync(db);
        var (loan, book) = await AddOpenLoanAsync(db, central);
        await Section1.AddStaffAsync(db, "desk-central", central);
        var controller = LoansControllerFactory.Create(db);
        Section1.SignIn(controller, "desk-central", "Reception");

        await controller.Return(new ReturnViewModel { LoanId = loan.Id, ReturnStatus = ItemStatus.Available });

        Assert.NotNull(loan.ReturnedAt);
        Assert.Equal(central.Id, book.BranchId);
        Assert.Equal(0, await db.ItemTransfers.CountAsync());
    }

    [Fact]
    public async Task Return_AtADifferentBranch_MovesItemThere_RecordsTheMove_AndKeepsLoanAndFineIntact()
    {
        using var db = TestDb.Create();
        var (central, north, _) = await Section1.SeedBranchesAsync(db);
        var (loan, book) = await AddOpenLoanAsync(db, central, daysOverdue: 3);
        await Section1.AddStaffAsync(db, "desk-north", north);
        var controller = LoansControllerFactory.Create(db);
        Section1.SignIn(controller, "desk-north", "Reception");

        var result = await controller.Return(new ReturnViewModel { LoanId = loan.Id, ReturnStatus = ItemStatus.Available });

        Assert.IsType<RedirectToActionResult>(result);
        Assert.NotNull(loan.ReturnedAt);
        Assert.Equal(3 * LibraryRules.FinePerLateDay, loan.FineAmount);
        Assert.Equal(central.Id, loan.BranchId); // issuing branch is history
        Assert.Equal(ItemStatus.Available, book.Status);
        Assert.Equal(north.Id, book.BranchId);  // the item is now where it was handed in

        var record = await db.ItemTransfers.SingleAsync();
        Assert.Equal(TransferKind.CrossBranchReturn, record.Kind);
        Assert.Equal(TransferStatus.Completed, record.Status);
        Assert.Equal(central.Id, record.FromBranchId);
        Assert.Equal(north.Id, record.ToBranchId);
        Assert.Equal("desk-north", record.ClosedBy);
        Assert.Contains($"loan #{loan.Id}", record.Reason);
        Assert.Contains("Northside Branch", (string)controller.TempData["Success"]!);
    }

    [Fact]
    public async Task Return_AtADifferentBranch_CancelsATransferRequestThatWasStillWaitingForTheItem()
    {
        using var db = TestDb.Create();
        var (central, north, river) = await Section1.SeedBranchesAsync(db);
        var book = await Section1.AddBookAsync(db, "B1", central);
        var borrower = await Section1.AddBorrowerAsync(db);
        var service = new TransferService(db);
        var requestId = (await service.RequestAsync(book.Id, river.Id, null, "admin")).Transfer!.Id;
        await LoansControllerFactory.Create(db).Borrow(new BorrowViewModel { LibraryCode = "B1", BorrowerId = borrower.Id });
        await Section1.AddStaffAsync(db, "desk-north", north);
        var controller = LoansControllerFactory.Create(db);
        Section1.SignIn(controller, "desk-north", "Reception");

        await controller.Return(new ReturnViewModel { LoanId = (await db.Loans.SingleAsync()).Id, ReturnStatus = ItemStatus.Available });

        var request = await db.ItemTransfers.SingleAsync(t => t.Id == requestId);
        Assert.Equal(TransferStatus.Cancelled, request.Status);
        Assert.Contains("returned at Northside Branch", request.ClosureNote);
        Assert.Equal(north.Id, book.BranchId);
        // The item is free to be transferred again from where it now is.
        Assert.True((await service.RequestAsync(book.Id, river.Id, null, "admin")).Succeeded);
    }

    [Fact]
    public async Task Return_DamagedAtADifferentBranch_StillRecordsLocationAndStatus()
    {
        using var db = TestDb.Create();
        var (central, north, _) = await Section1.SeedBranchesAsync(db);
        var (loan, book) = await AddOpenLoanAsync(db, central);
        await Section1.AddStaffAsync(db, "desk-north", north);
        var controller = LoansControllerFactory.Create(db);
        Section1.SignIn(controller, "desk-north", "Reception");

        await controller.Return(new ReturnViewModel { LoanId = loan.Id, ReturnStatus = ItemStatus.Damaged });

        Assert.Equal(ItemStatus.Damaged, book.Status);
        Assert.Equal(north.Id, book.BranchId);
    }

    [Fact]
    public async Task RealSqlite_Return_WhenItemChangedBySomeoneElse_FailsCleanlyWithoutAnException()
    {
        using var sqlite = new SqliteTestDb();
        int loanId, itemId;
        using (var seed = sqlite.Open())
        {
            var (central, _, _) = await Section1.SeedBranchesAsync(seed);
            var (loan, book) = await AddOpenLoanAsync(seed, central);
            (loanId, itemId) = (loan.Id, book.Id);
        }

        using var deskDb = sqlite.Open();
        _ = await deskDb.Loans.Include(l => l.Item).SingleAsync(l => l.Id == loanId);
        using (var adminDb = sqlite.Open())
        {
            // An Admin edits the item's status while the return form is open.
            (await adminDb.Items.SingleAsync(i => i.Id == itemId)).Status = ItemStatus.Damaged;
            await adminDb.SaveChangesAsync();
        }
        var controller = LoansControllerFactory.Create(deskDb);
        Section1.SignIn(controller, "nobody", "Admin");

        var result = await controller.Return(new ReturnViewModel { LoanId = loanId, ReturnStatus = ItemStatus.Available });

        Assert.IsType<RedirectToActionResult>(result);
        Assert.Contains("changed by someone else", (string)controller.TempData["Error"]!);
        using var verify = sqlite.Open();
        Assert.Null((await verify.Loans.SingleAsync()).ReturnedAt);
    }

    [Fact]
    public async Task PublicSearch_FiltersByBranch()
    {
        using var db = TestDb.Create();
        var (central, north, _) = await Section1.SeedBranchesAsync(db);
        await Section1.AddBookAsync(db, "B1", central);
        await Section1.AddBookAsync(db, "B2", north);
        var controller = new HomeController(db);

        var view = (ViewResult)await controller.Index(q: null, type: null, availableOnly: false, branchId: north.Id);

        Assert.Equal("B2", Assert.Single((IEnumerable<Item>)view.Model!).LibraryCode);
    }

    [Fact]
    public async Task Staff_CanBeAssignedToABranch_AndReassigned()
    {
        using var host = await IdentityTestHost.CreateAsync();
        var (_, north, river) = await Section1.SeedBranchesAsync(host.Db);
        var controller = new StaffController(host.UserManager, host.Db);
        ControllerTestHelpers.AttachContext(controller);

        await controller.Create(new StaffFormViewModel { UserName = "desk1", Role = "Reception", Password = "Passw0rd!", BranchId = north.Id });
        var user = await host.UserManager.FindByNameAsync("desk1");
        Assert.Equal(north.Id, user!.BranchId);

        await controller.Edit(new StaffFormViewModel { Id = user.Id, UserName = "desk1", Role = "Reception", BranchId = river.Id });
        Assert.Equal(river.Id, (await host.UserManager.FindByNameAsync("desk1"))!.BranchId);
        Assert.True(await host.UserManager.IsInRoleAsync(user, "Reception"));
        Assert.True(await host.UserManager.CheckPasswordAsync(user, "Passw0rd!"));
    }

    [Fact]
    public async Task Staff_ReceptionAccountMustHaveABranch_OtherRolesNeedNot()
    {
        using var host = await IdentityTestHost.CreateAsync();
        await Section1.SeedBranchesAsync(host.Db);
        var controller = new StaffController(host.UserManager, host.Db);
        ControllerTestHelpers.AttachContext(controller);

        await controller.Create(new StaffFormViewModel { UserName = "desk1", Role = "Reception", Password = "Passw0rd!" });
        Assert.True(controller.ModelState.ContainsKey(nameof(StaffFormViewModel.BranchId)));
        Assert.Null(await host.UserManager.FindByNameAsync("desk1"));

        var second = new StaffController(host.UserManager, host.Db);
        ControllerTestHelpers.AttachContext(second);
        await second.Create(new StaffFormViewModel { UserName = "boss", Role = "Manager", Password = "Passw0rd!" });
        Assert.NotNull(await host.UserManager.FindByNameAsync("boss"));
    }

    [Fact]
    public async Task Staff_UnknownBranch_IsRejected()
    {
        using var host = await IdentityTestHost.CreateAsync();
        var controller = new StaffController(host.UserManager, host.Db);
        ControllerTestHelpers.AttachContext(controller);

        await controller.Create(new StaffFormViewModel { UserName = "desk1", Role = "Reception", Password = "Passw0rd!", BranchId = 9999 });

        Assert.True(controller.ModelState.ContainsKey(nameof(StaffFormViewModel.BranchId)));
        Assert.Null(await host.UserManager.FindByNameAsync("desk1"));
    }
}
