using LibraryManagementSystem.Controllers;
using LibraryManagementSystem.Models;
using LibraryManagementSystem.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace LibraryManagementSystem.Tests;

public class ItemsControllerTests
{
    [Fact]
    public async Task Create_Book_MissingAuthor_AddsModelErrorAndDoesNotSave()
    {
        using var db = TestDb.Create();
        var controller = new ItemsController(db);
        var model = new ItemFormViewModel { ItemType = "Book", Name = "N", Description = "D", LibraryCode = "X1", BookGenre = "Fantasy" };

        await controller.Create(model);

        Assert.False(controller.ModelState.IsValid);
        Assert.True(controller.ModelState.ContainsKey(nameof(model.Author)));
        Assert.Equal(0, await db.Items.CountAsync());
    }

    [Fact]
    public async Task Create_Music_DoesNotRequireBookOrToyFields()
    {
        using var db = TestDb.Create();
        var controller = new ItemsController(db);
        var model = new ItemFormViewModel { ItemType = "Music", Name = "Song", Description = "D", LibraryCode = "M900", Artist = "Artist" };

        var result = await controller.Create(model);

        Assert.True(controller.ModelState.IsValid);
        Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(1, await db.Items.CountAsync());
    }

    [Fact]
    public async Task Create_Toy_MissingMinimumAge_AddsModelError()
    {
        using var db = TestDb.Create();
        var controller = new ItemsController(db);
        var model = new ItemFormViewModel { ItemType = "Toy", Name = "Blocks", Description = "D", LibraryCode = "T900", ToyType = "Construction" };

        await controller.Create(model);

        Assert.False(controller.ModelState.IsValid);
        Assert.True(controller.ModelState.ContainsKey(nameof(model.MinimumAge)));
    }

    [Fact]
    public async Task Create_DuplicateLibraryCode_AddsModelErrorAndDoesNotSave()
    {
        using var db = TestDb.Create();
        db.Items.Add(new Book { Name = "A", Description = "d", LibraryCode = "DUP1", Status = ItemStatus.Available, Author = "a", Genre = "g" });
        await db.SaveChangesAsync();

        var controller = new ItemsController(db);
        var model = new ItemFormViewModel { ItemType = "Book", Name = "B", Description = "d", LibraryCode = "DUP1", Author = "a2", BookGenre = "g2" };

        await controller.Create(model);

        Assert.False(controller.ModelState.IsValid);
        Assert.Equal(1, await db.Items.CountAsync());
    }

    [Fact]
    public async Task Delete_ItemWithLoanHistory_IsBlocked()
    {
        using var db = TestDb.Create();
        var item = new Book { Name = "A", Description = "d", LibraryCode = "B950", Status = ItemStatus.Available, Author = "a", Genre = "g" };
        var borrower = new Borrower { FullName = "Test", Email = "t@example.com", Phone = "0400000000", Address = "1 St" };
        db.Items.Add(item);
        db.Borrowers.Add(borrower);
        await db.SaveChangesAsync();
        db.Loans.Add(new Loan { ItemId = item.Id, BorrowerId = borrower.Id, BorrowedAt = DateTime.Now.AddDays(-30), DueDate = DateTime.Now.AddDays(-16), ReturnedAt = DateTime.Now.AddDays(-10) });
        await db.SaveChangesAsync();

        var controller = new ItemsController(db);
        ControllerTestHelpers.AttachContext(controller);

        await controller.DeleteConfirmed(item.Id);

        Assert.Equal(1, await db.Items.CountAsync());
    }
}
