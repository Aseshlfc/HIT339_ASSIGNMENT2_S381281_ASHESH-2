using LibraryManagementSystem.Controllers;
using LibraryManagementSystem.Models;
using LibraryManagementSystem.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Reflection;

namespace LibraryManagementSystem.Tests;

public class BranchesControllerTests
{
    private static BranchesController Controller(Data.LibraryContext db)
    {
        var controller = new BranchesController(db);
        Section1.SignIn(controller, "admin", "Admin");
        return controller;
    }

    [Fact]
    public async Task Create_ValidBranch_SavesWithUpperCaseCodeAndActive()
    {
        using var db = TestDb.Create();
        var controller = Controller(db);

        var result = await controller.Create(new Branch { Code = " wst ", Name = "Westside Branch", Address = "5 West Rd" });

        Assert.IsType<RedirectToActionResult>(result);
        var saved = await db.Branches.SingleAsync();
        Assert.Equal("WST", saved.Code);
        Assert.True(saved.IsActive);
    }

    [Fact]
    public async Task Create_DuplicateCode_AddsModelErrorAndDoesNotSave()
    {
        using var db = TestDb.Create();
        await Section1.SeedBranchesAsync(db);
        var controller = Controller(db);

        var result = await controller.Create(new Branch { Code = "nth", Name = "Another Branch", Address = "1 Rd" });

        Assert.IsType<ViewResult>(result);
        Assert.True(controller.ModelState.ContainsKey(nameof(Branch.Code)));
        Assert.Equal(3, await db.Branches.CountAsync());
    }

    [Fact]
    public async Task Create_DuplicateName_AddsModelError()
    {
        using var db = TestDb.Create();
        await Section1.SeedBranchesAsync(db);
        var controller = Controller(db);

        await controller.Create(new Branch { Code = "NEW", Name = "northside branch", Address = "1 Rd" });

        Assert.True(controller.ModelState.ContainsKey(nameof(Branch.Name)));
    }

    [Theory]
    [InlineData("", "Name", "Address")]
    [InlineData("CODE", "", "Address")]
    [InlineData("CODE", "Name", "")]
    [InlineData("BAD CODE!", "Name", "Address")]
    [InlineData("WAYTOOLONGCODE", "Name", "Address")]
    public void Branch_InvalidInput_FailsValidation(string code, string name, string address)
    {
        var branch = new Branch { Code = code, Name = name, Address = address };
        var results = new List<System.ComponentModel.DataAnnotations.ValidationResult>();

        var valid = System.ComponentModel.DataAnnotations.Validator.TryValidateObject(
            branch, new System.ComponentModel.DataAnnotations.ValidationContext(branch), results, validateAllProperties: true);

        Assert.False(valid);
    }

    [Fact]
    public async Task Edit_UpdatesDetailsButNotActiveFlag()
    {
        using var db = TestDb.Create();
        var (_, north, _) = await Section1.SeedBranchesAsync(db);
        var controller = Controller(db);

        var result = await controller.Edit(new Branch { Id = north.Id, Code = "NTH", Name = "Northside Library", Address = "New address", IsActive = false });

        Assert.IsType<RedirectToActionResult>(result);
        var saved = await db.Branches.SingleAsync(b => b.Id == north.Id);
        Assert.Equal("Northside Library", saved.Name);
        Assert.Equal("New address", saved.Address);
        Assert.True(saved.IsActive);
    }

    [Fact]
    public async Task Index_ListsEveryBranchWithItemAndStaffCounts()
    {
        using var db = TestDb.Create();
        var (central, north, _) = await Section1.SeedBranchesAsync(db);
        await Section1.AddBookAsync(db, "B1", north);
        await Section1.AddBookAsync(db, "B2", north, ItemStatus.Damaged);
        await Section1.AddBookAsync(db, "B3", central);
        await Section1.AddStaffAsync(db, "desk-north", north);
        var controller = Controller(db);

        var view = Assert.IsType<ViewResult>(await controller.Index());
        var rows = Assert.IsAssignableFrom<IEnumerable<BranchListItemViewModel>>(view.Model).ToList();

        Assert.Equal(3, rows.Count);
        var northRow = rows.Single(r => r.Branch.Id == north.Id);
        Assert.Equal(2, northRow.ItemCount);
        Assert.Equal(1, northRow.AvailableCount);
        Assert.Equal(1, northRow.StaffCount);
    }

    [Fact]
    public async Task Details_ShowsOnlyThatBranchsInventory_AndFiltersByStatus()
    {
        using var db = TestDb.Create();
        var (central, north, _) = await Section1.SeedBranchesAsync(db);
        await Section1.AddBookAsync(db, "B1", north);
        await Section1.AddBookAsync(db, "B2", north, ItemStatus.Damaged);
        await Section1.AddBookAsync(db, "B3", central);
        var controller = Controller(db);

        var all = (BranchDetailsViewModel)((ViewResult)await controller.Details(north.Id)).Model!;
        var damaged = (BranchDetailsViewModel)((ViewResult)await controller.Details(north.Id, ItemStatus.Damaged)).Model!;

        Assert.Equal(new[] { "B1", "B2" }, all.Items.Select(i => i.LibraryCode));
        Assert.Equal(new[] { "B2" }, damaged.Items.Select(i => i.LibraryCode));
    }

    [Fact]
    public async Task Delete_BranchHoldingItems_IsBlocked()
    {
        using var db = TestDb.Create();
        var (_, north, _) = await Section1.SeedBranchesAsync(db);
        await Section1.AddBookAsync(db, "B1", north);
        var controller = Controller(db);

        await controller.DeleteConfirmed(north.Id);

        Assert.True(await db.Branches.AnyAsync(b => b.Id == north.Id));
        Assert.Contains("still holds items", (string)controller.TempData["Error"]!);
    }

    [Fact]
    public async Task Delete_BranchWithStaff_IsBlocked()
    {
        using var db = TestDb.Create();
        var (_, north, _) = await Section1.SeedBranchesAsync(db);
        await Section1.AddStaffAsync(db, "desk-north", north);
        var controller = Controller(db);

        await controller.DeleteConfirmed(north.Id);

        Assert.True(await db.Branches.AnyAsync(b => b.Id == north.Id));
        Assert.Contains("staff assigned", (string)controller.TempData["Error"]!);
    }

    [Fact]
    public async Task Delete_BranchWithTransferHistory_IsBlocked()
    {
        using var db = TestDb.Create();
        var (central, north, _) = await Section1.SeedBranchesAsync(db);
        var book = await Section1.AddBookAsync(db, "B1", central);
        db.ItemTransfers.Add(new ItemTransfer { ItemId = book.Id, FromBranchId = north.Id, ToBranchId = central.Id, Status = TransferStatus.Completed, RequestedBy = "x" });
        await db.SaveChangesAsync();
        var controller = Controller(db);

        await controller.DeleteConfirmed(north.Id);

        Assert.True(await db.Branches.AnyAsync(b => b.Id == north.Id));
        Assert.Contains("transfer history", (string)controller.TempData["Error"]!);
    }

    [Fact]
    public async Task Delete_UnusedBranch_Succeeds_ButDefaultBranchCannotBeDeleted()
    {
        using var db = TestDb.Create();
        var (central, north, _) = await Section1.SeedBranchesAsync(db);
        var controller = Controller(db);

        await controller.DeleteConfirmed(north.Id);
        await controller.DeleteConfirmed(central.Id);

        Assert.False(await db.Branches.AnyAsync(b => b.Id == north.Id));
        Assert.True(await db.Branches.AnyAsync(b => b.Id == central.Id));
    }

    [Fact]
    public async Task ToggleActive_BlockedWhileBranchHoldsItems_AllowedWhenEmpty()
    {
        using var db = TestDb.Create();
        var (_, north, river) = await Section1.SeedBranchesAsync(db);
        await Section1.AddBookAsync(db, "B1", north);
        var controller = Controller(db);

        await controller.ToggleActive(north.Id);
        await controller.ToggleActive(river.Id);

        Assert.True((await db.Branches.SingleAsync(b => b.Id == north.Id)).IsActive);
        Assert.False((await db.Branches.SingleAsync(b => b.Id == river.Id)).IsActive);
    }

    [Fact]
    public void BranchChanges_AreRestrictedToAdmin_AndViewingToStaff()
    {
        var classRoles = typeof(BranchesController).GetCustomAttribute<AuthorizeAttribute>()!.Roles;
        Assert.Equal("Admin,Reception,Manager", classRoles);

        foreach (var name in new[] { "Create", "Edit", "ToggleActive", "Delete", "DeleteConfirmed" })
        {
            foreach (var method in typeof(BranchesController).GetMethods().Where(m => m.Name == name))
                Assert.Equal("Admin", method.GetCustomAttribute<AuthorizeAttribute>()?.Roles);
        }
    }
}
