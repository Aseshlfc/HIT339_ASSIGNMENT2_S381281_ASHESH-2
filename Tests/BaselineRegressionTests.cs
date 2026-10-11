using LibraryManagementSystem.Controllers;
using LibraryManagementSystem.Models;
using LibraryManagementSystem.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Reflection;

namespace LibraryManagementSystem.Tests;

/// <summary>
/// Assignment 2 behaviour that had no automated test before the multi-branch
/// work, pinned here so later changes cannot quietly break it.
/// </summary>
public class BaselineRegressionTests
{
    [Fact]
    public async Task Borrowers_CreateEditAndDelete()
    {
        using var db = TestDb.Create();
        var controller = new BorrowersController(db);
        ControllerTestHelpers.AttachContext(controller);

        await controller.Create(new Borrower { FullName = "Ann", Email = "ann@example.com", Phone = "0400000000", Address = "1 St" });
        var saved = await db.Borrowers.SingleAsync();
        db.ChangeTracker.Clear();

        await controller.Edit(new Borrower { Id = saved.Id, FullName = "Ann Lee", Email = "ann@example.com", Phone = "0400000000", Address = "2 St" });
        Assert.Equal("Ann Lee", (await db.Borrowers.SingleAsync()).FullName);

        await controller.DeleteConfirmed(saved.Id);
        Assert.Equal(0, await db.Borrowers.CountAsync());
    }

    [Fact]
    public async Task Borrowers_WithLoanHistory_CannotBeDeleted()
    {
        using var db = TestDb.Create();
        var borrower = await Section1.AddBorrowerAsync(db);
        var book = new Book { Name = "n", Description = "d", LibraryCode = "B1", Author = "a", Genre = "g" };
        db.Items.Add(book);
        await db.SaveChangesAsync();
        db.Loans.Add(new Loan { ItemId = book.Id, BorrowerId = borrower.Id, BorrowedAt = DateTime.Now, DueDate = DateTime.Now.AddDays(14) });
        await db.SaveChangesAsync();
        var controller = new BorrowersController(db);
        ControllerTestHelpers.AttachContext(controller);

        await controller.DeleteConfirmed(borrower.Id);

        Assert.Equal(1, await db.Borrowers.CountAsync());
    }

    [Theory]
    [InlineData("Book")]
    [InlineData("Music")]
    [InlineData("Toy")]
    public async Task Items_EachTypeCanBeCreatedEditedAndDeleted(string type)
    {
        using var db = TestDb.Create();
        ItemFormViewModel Form(int? id, string name) => new()
        {
            Id = id, ItemType = type, Name = name, Description = "d", LibraryCode = "X1",
            Author = "a", BookGenre = "g", Artist = "ar", ToyType = "t", MinimumAge = 3
        };

        Assert.IsType<RedirectToActionResult>(await new ItemsController(db).Create(Form(null, "First")));
        var item = await db.Items.SingleAsync();
        Assert.Equal(type, item.ItemType);
        Assert.Equal(ItemStatus.Available, item.Status);

        Assert.IsType<RedirectToActionResult>(await new ItemsController(db).Edit(Form(item.Id, "Second")));
        Assert.Equal("Second", (await db.Items.SingleAsync()).Name);

        var controller = new ItemsController(db);
        ControllerTestHelpers.AttachContext(controller);
        await controller.DeleteConfirmed(item.Id);
        Assert.Equal(0, await db.Items.CountAsync());
    }

    [Fact]
    public async Task Items_TypeCannotBeChangedAfterCreation()
    {
        using var db = TestDb.Create();
        var book = new Book { Name = "n", Description = "d", LibraryCode = "B1", Author = "a", Genre = "g" };
        db.Items.Add(book);
        await db.SaveChangesAsync();
        var controller = new ItemsController(db);

        await controller.Edit(new ItemFormViewModel { Id = book.Id, ItemType = "Music", Name = "n", Description = "d", LibraryCode = "B1", Artist = "x" });

        Assert.False(controller.ModelState.IsValid);
    }

    [Fact]
    public async Task Loans_FineIsOneDollarPerLateDay_AndCanBeMarkedPaidOnce()
    {
        using var db = TestDb.Create();
        var borrower = await Section1.AddBorrowerAsync(db);
        var book = new Book { Name = "n", Description = "d", LibraryCode = "B1", Author = "a", Genre = "g", Status = ItemStatus.Borrowed };
        db.Items.Add(book);
        await db.SaveChangesAsync();
        var loan = new Loan { ItemId = book.Id, BorrowerId = borrower.Id, BorrowedAt = DateTime.Now.AddDays(-21), DueDate = DateTime.Now.Date.AddDays(-7) };
        db.Loans.Add(loan);
        await db.SaveChangesAsync();
        var controller = new LoansController(db);

        await controller.Return(new ReturnViewModel { LoanId = loan.Id, ReturnStatus = ItemStatus.Available });
        Assert.Equal(7.00m, loan.FineAmount);
        Assert.False(loan.FinePaid);

        await controller.MarkFinePaid(loan.Id);
        Assert.True(loan.FinePaid);
        Assert.Equal(7.00m, loan.FineAmount);
    }

    [Fact]
    public void RoleRestrictions_OnEveryStaffController_AreUnchanged()
    {
        string? Roles(Type t) => t.GetCustomAttribute<AuthorizeAttribute>()?.Roles;

        Assert.Equal("Admin", Roles(typeof(ItemsController)));
        Assert.Equal("Admin", Roles(typeof(StaffController)));
        Assert.Equal("Reception", Roles(typeof(LoansController)));
        Assert.Equal("Reception", Roles(typeof(BorrowersController)));
        Assert.Equal("Manager", Roles(typeof(ManagerController)));
        Assert.Null(typeof(HomeController).GetCustomAttribute<AuthorizeAttribute>());
    }

    [Fact]
    public void EveryPostAction_RequiresAnAntiForgeryToken()
    {
        var posts = typeof(HomeController).Assembly.GetTypes()
            .Where(t => typeof(Controller).IsAssignableFrom(t))
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Where(m => m.GetCustomAttribute<HttpPostAttribute>() != null)
            .ToList();

        Assert.NotEmpty(posts);
        Assert.All(posts, m => Assert.True(
            m.GetCustomAttribute<ValidateAntiForgeryTokenAttribute>() != null,
            $"{m.DeclaringType!.Name}.{m.Name} is a POST without [ValidateAntiForgeryToken]"));
    }

    [Fact]
    public void PublicErrorPage_Exists()
    {
        using var db = TestDb.Create();
        Assert.IsType<ViewResult>(new HomeController(db).Error());
    }
}
