using LibraryManagementSystem.Controllers;
using LibraryManagementSystem.Models;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace LibraryManagementSystem.Tests;

public class HomeControllerTests
{
    [Fact]
    public async Task Search_IsCaseInsensitive()
    {
        using var db = TestDb.Create();
        db.Items.Add(new Book { Name = "The Hobbit", Description = "d", LibraryCode = "B001", Status = ItemStatus.Available, Author = "Tolkien", Genre = "Fantasy" });
        await db.SaveChangesAsync();

        var controller = new HomeController(db);
        var result = await controller.Index(q: "hobbit", type: null, availableOnly: false);

        var view = Assert.IsType<ViewResult>(result);
        var items = Assert.IsAssignableFrom<IEnumerable<Item>>(view.Model);
        Assert.Single(items);
    }

    [Fact]
    public async Task Search_SupportsPartialMatch()
    {
        using var db = TestDb.Create();
        db.Items.Add(new Book { Name = "The Great Gatsby", Description = "d", LibraryCode = "B002", Status = ItemStatus.Available, Author = "Fitzgerald", Genre = "Classic" });
        await db.SaveChangesAsync();

        var controller = new HomeController(db);
        var result = await controller.Index(q: "Gats", type: null, availableOnly: false);

        var view = Assert.IsType<ViewResult>(result);
        var items = Assert.IsAssignableFrom<IEnumerable<Item>>(view.Model);
        Assert.Single(items);
    }

    [Fact]
    public async Task Search_ExcludesDestroyedItems()
    {
        using var db = TestDb.Create();
        db.Items.Add(new Book { Name = "Old Book", Description = "d", LibraryCode = "B003", Status = ItemStatus.Destroy, Author = "a", Genre = "g" });
        await db.SaveChangesAsync();

        var controller = new HomeController(db);
        var result = await controller.Index(q: null, type: null, availableOnly: false);

        var view = Assert.IsType<ViewResult>(result);
        var items = Assert.IsAssignableFrom<IEnumerable<Item>>(view.Model);
        Assert.Empty(items);
    }

    [Fact]
    public async Task Search_FiltersByItemType()
    {
        using var db = TestDb.Create();
        db.Items.Add(new Book { Name = "A Book", Description = "d", LibraryCode = "B004", Status = ItemStatus.Available, Author = "a", Genre = "g" });
        db.Items.Add(new Music { Name = "A Song", Description = "d", LibraryCode = "M004", Status = ItemStatus.Available, Artist = "a" });
        await db.SaveChangesAsync();

        var controller = new HomeController(db);
        var result = await controller.Index(q: null, type: "Music", availableOnly: false);

        var view = Assert.IsType<ViewResult>(result);
        var items = Assert.IsAssignableFrom<IEnumerable<Item>>(view.Model);
        var single = Assert.Single(items);
        Assert.IsType<Music>(single);
    }

    [Fact]
    public async Task Search_AvailableOnly_ExcludesBorrowedItems()
    {
        using var db = TestDb.Create();
        db.Items.Add(new Book { Name = "Available Book", Description = "d", LibraryCode = "B005", Status = ItemStatus.Available, Author = "a", Genre = "g" });
        db.Items.Add(new Book { Name = "Borrowed Book", Description = "d", LibraryCode = "B006", Status = ItemStatus.Borrowed, Author = "a", Genre = "g" });
        await db.SaveChangesAsync();

        var controller = new HomeController(db);
        var result = await controller.Index(q: null, type: null, availableOnly: true);

        var view = Assert.IsType<ViewResult>(result);
        var items = Assert.IsAssignableFrom<IEnumerable<Item>>(view.Model);
        var single = Assert.Single(items);
        Assert.Equal("Available Book", single.Name);
    }
}
