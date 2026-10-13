using System.Reflection;
using LibraryManagementSystem.Api;
using LibraryManagementSystem.Controllers;
using LibraryManagementSystem.Data;
using LibraryManagementSystem.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using Xunit;

namespace LibraryManagementSystem.Tests;

public class CatalogueApiTests
{
    private static CatalogueApiController Create(LibraryContext db, int maxPageSize = 100) =>
        new(db, Options.Create(new PublicApiOptions { ApiKeys = ["k"], MaxPageSize = maxPageSize, DefaultPageSize = 20 }));

    private static T Ok<T>(ActionResult<T> result)
    {
        Assert.NotNull(result.Value);
        return result.Value!;
    }

    private static int StatusOf(ActionResult result) => ((ObjectResult)result).StatusCode!.Value;

    /// <summary>Three branches and a small catalogue: 4 books, 2 music, 2 toys, 1 destroyed book.</summary>
    private static async Task SeedAsync(LibraryContext db)
    {
        var (central, north, river) = await Section1.SeedBranchesAsync(db);
        db.Items.AddRange(
            new Book { Name = "Dune", Description = "Desert planet.", LibraryCode = "B1", Status = ItemStatus.Borrowed, Author = "Frank Herbert", Genre = "Science Fiction", PublicationYear = 1965, BranchId = central.Id },
            new Book { Name = "Emma", Description = "Matchmaking.", LibraryCode = "B2", Status = ItemStatus.Available, Author = "Jane Austen", Genre = "Romance", PublicationYear = 1815, BranchId = north.Id },
            new Book { Name = "Persuasion", Description = "Second chances.", LibraryCode = "B3", Status = ItemStatus.Available, Author = "Jane Austen", Genre = "romance", PublicationYear = 1817, BranchId = central.Id },
            new Book { Name = "Hidden", Description = "Written off.", LibraryCode = "B4", Status = ItemStatus.Destroy, Author = "Nobody", Genre = "Romance", BranchId = central.Id },
            new Book { Name = "Dracula", Description = "A count.", LibraryCode = "B5", Status = ItemStatus.InTransit, Author = "Bram Stoker", Genre = "Horror", PublicationYear = 1897, BranchId = river.Id },
            new Music { Name = "Abbey Road", Description = "Rock classic.", LibraryCode = "M1", Status = ItemStatus.Available, Artist = "The Beatles", Genre = "Rock", ReleaseYear = 1969, BranchId = central.Id },
            new Music { Name = "Mystery Mix", Description = "No genre given.", LibraryCode = "M2", Status = ItemStatus.Damaged, Artist = "Various", Genre = "", BranchId = river.Id },
            new Toy { Name = "Jigsaw", Description = "500 pieces.", LibraryCode = "T1", Status = ItemStatus.Available, ToyType = "Puzzle", MinimumAge = 8, Manufacturer = "Acme", BranchId = north.Id },
            new Toy { Name = "Blocks", Description = "Wooden blocks.", LibraryCode = "T2", Status = ItemStatus.Available, ToyType = "Construction", MinimumAge = 3, Manufacturer = "", BranchId = central.Id });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Items_NeverIncludeDestroyedItems_AndAreSortedByName()
    {
        using var testDb = new SqliteTestDb();
        using var db = testDb.Open();
        await SeedAsync(db);

        var page = Ok(await Create(db).GetItems());

        Assert.Equal(8, page.TotalItems);
        Assert.Equal(new[] { "Abbey Road", "Blocks", "Dracula", "Dune", "Emma", "Jigsaw", "Mystery Mix", "Persuasion" },
            page.Items.Select(i => i.Name).ToArray());
        Assert.DoesNotContain(page.Items, i => i.LibraryCode == "B4");
    }

    [Fact]
    public async Task Items_ArePaged()
    {
        using var testDb = new SqliteTestDb();
        using var db = testDb.Open();
        await SeedAsync(db);
        var api = Create(db);

        var second = Ok(await api.GetItems(page: 2, pageSize: 3));
        var last = Ok(await api.GetItems(page: 3, pageSize: 3));
        var beyond = Ok(await api.GetItems(page: 9, pageSize: 3));

        Assert.Equal(3, second.TotalPages);
        Assert.Equal(new[] { "Dune", "Emma", "Jigsaw" }, second.Items.Select(i => i.Name).ToArray());
        Assert.Equal(2, last.Items.Count);
        Assert.Empty(beyond.Items);
        Assert.Equal(8, beyond.TotalItems);
    }

    [Fact]
    public async Task Items_FilterByTypeCategoryBranchStatusAndText()
    {
        using var testDb = new SqliteTestDb();
        using var db = testDb.Open();
        await SeedAsync(db);
        var api = Create(db);

        Assert.Equal(2, Ok(await api.GetItems(type: "toy")).TotalItems);
        Assert.Equal(2, Ok(await api.GetItems(category: "ROMANCE")).TotalItems);   // case-insensitive; the destroyed one is excluded
        Assert.Equal(1, Ok(await api.GetItems(category: "Puzzle")).TotalItems);    // a toy's type counts as its category
        Assert.Equal(4, Ok(await api.GetItems(branch: "cen")).TotalItems);
        Assert.Equal(2, Ok(await api.GetItems(branch: "NTH")).TotalItems);
        Assert.Equal(new[] { "B1" }, Ok(await api.GetItems(status: "borrowed")).Items.Select(i => i.LibraryCode).ToArray());
        Assert.Equal(new[] { "B5" }, Ok(await api.GetItems(status: "InTransit")).Items.Select(i => i.LibraryCode).ToArray());
        Assert.Equal(5, Ok(await api.GetItems(availableOnly: true)).TotalItems);
        Assert.Equal(new[] { "M1" }, Ok(await api.GetItems(q: "ROCK")).Items.Select(i => i.LibraryCode).ToArray());   // matches the description
        Assert.Equal(new[] { "T1" }, Ok(await api.GetItems(q: "t1")).Items.Select(i => i.LibraryCode).ToArray());     // matches the library code
        Assert.Equal(new[] { "B2" }, Ok(await api.GetItems(type: "book", branch: "NTH", availableOnly: true)).Items.Select(i => i.LibraryCode).ToArray());
    }

    [Fact]
    public async Task Items_RejectBadInputWithAProblemResponse()
    {
        using var testDb = new SqliteTestDb();
        using var db = testDb.Open();
        await SeedAsync(db);
        var api = Create(db, maxPageSize: 50);

        Assert.Equal(400, StatusOf((await api.GetItems(type: "dvd")).Result!));
        Assert.Equal(400, StatusOf((await api.GetItems(status: "Destroy")).Result!));
        Assert.Equal(400, StatusOf((await api.GetItems(status: "lost")).Result!));
        Assert.Equal(400, StatusOf((await api.GetItems(page: 0)).Result!));
        Assert.Equal(400, StatusOf((await api.GetItems(pageSize: 51)).Result!));
        Assert.Equal(400, StatusOf((await api.GetItems(pageSize: -1)).Result!));
        Assert.Equal(404, StatusOf((await api.GetItems(branch: "ZZZ")).Result!));
    }

    [Fact]
    public async Task Item_IsFoundByLibraryCodeIgnoringCase_AndDestroyedItemsAreNotFound()
    {
        using var testDb = new SqliteTestDb();
        using var db = testDb.Open();
        await SeedAsync(db);
        var api = Create(db);

        var item = Ok(await api.GetItem("b2"));
        Assert.Equal("Emma", item.Name);
        Assert.Equal("Jane Austen", item.Author);
        Assert.Equal("NTH", item.Branch!.Code);
        Assert.Equal(404, StatusOf((await api.GetItem("B4")).Result!));
        Assert.Equal(404, StatusOf((await api.GetItem("NOPE")).Result!));
    }

    [Fact]
    public async Task Item_ShowsOnlyTheFieldsThatApplyToItsType()
    {
        using var testDb = new SqliteTestDb();
        using var db = testDb.Open();
        await SeedAsync(db);
        var api = Create(db);

        var book = Ok(await api.GetItem("B1"));
        var music = Ok(await api.GetItem("M1"));
        var toy = Ok(await api.GetItem("T1"));

        Assert.Equal("Frank Herbert", book.Author);
        Assert.Null(book.Artist);
        Assert.Null(book.ToyType);
        Assert.Equal("The Beatles", music.Artist);
        Assert.Equal(1969, music.Year);
        Assert.Null(music.Author);
        Assert.Equal("Puzzle", toy.ToyType);
        Assert.Equal(8, toy.MinimumAge);
        Assert.Equal("Acme", toy.Manufacturer);
        Assert.Equal("Puzzle", toy.Category);
        Assert.False(book.IsAvailable);
        Assert.True(music.IsAvailable);
        Assert.Equal("Borrowed", book.Status);
    }

    [Fact]
    public async Task Item_DoesNotExposeInternalIdsOrBorrowerData()
    {
        var names = typeof(ItemDto).GetProperties().Select(p => p.Name).ToList();
        Assert.DoesNotContain("Id", names);
        Assert.DoesNotContain("BranchId", names);
        Assert.DoesNotContain("Loans", names);
        Assert.DoesNotContain("Borrower", names);
        await Task.CompletedTask;
    }

    [Fact]
    public async Task Categories_CountItemsByTypeAndGenre_ExcludingDestroyedItems()
    {
        using var testDb = new SqliteTestDb();
        using var db = testDb.Open();
        await SeedAsync(db);

        var categories = Ok(await Create(db).GetCategories());

        Assert.Equal(new[] { "Book", "Music", "Toy" }, categories.Select(c => c.Type).ToArray());
        var books = categories.Single(c => c.Type == "Book");
        Assert.Equal(4, books.ItemCount);                       // destroyed B4 excluded
        Assert.Equal(2, books.AvailableCount);
        Assert.Equal(2, books.Categories.Single(c => string.Equals(c.Name, "romance", StringComparison.OrdinalIgnoreCase)).ItemCount);   // "Romance" and "romance" are one genre
        var music = categories.Single(c => c.Type == "Music");
        Assert.Equal(1, music.Categories.Single(c => c.Name == "Uncategorised").ItemCount);
        Assert.Equal(2, categories.Single(c => c.Type == "Toy").Categories.Count);
    }

    [Fact]
    public async Task Status_ReportsTotalsAndEachBranch()
    {
        using var testDb = new SqliteTestDb();
        using var db = testDb.Open();
        await SeedAsync(db);

        var status = Ok(await Create(db).GetStatus());

        Assert.Equal("operational", status.Status);
        Assert.True(status.DatabaseReachable);
        Assert.Equal(8, status.Totals.Items);
        Assert.Equal(5, status.Totals.Available);
        Assert.Equal(1, status.Totals.Borrowed);
        Assert.Equal(1, status.Totals.Damaged);
        Assert.Equal(1, status.Totals.InTransit);
        Assert.Equal(3, status.Branches.Count);
        var central = status.Branches.Single(b => b.Code == "CEN");
        Assert.Equal(4, central.ItemCount);          // the destroyed book is not counted
        Assert.Equal(3, central.AvailableCount);
    }

    [Fact]
    public void EveryApiAction_IsReadOnly()
    {
        var actions = typeof(CatalogueApiController)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .ToList();

        Assert.NotEmpty(actions);
        foreach (var action in actions)
        {
            var verbs = action.GetCustomAttributes<HttpMethodAttribute>().SelectMany(a => a.HttpMethods).ToList();
            Assert.Equal(new[] { "GET" }, verbs.ToArray());
        }
    }
}

public class ApiKeyFilterTests
{
    private static ApiKeyFilter Filter(params string[] keys) =>
        new(Options.Create(new PublicApiOptions { ApiKeys = keys }));

    private static AuthorizationFilterContext Context(Action<DefaultHttpContext>? setup = null)
    {
        var http = new DefaultHttpContext();
        setup?.Invoke(http);
        return new AuthorizationFilterContext(new ActionContext(http, new RouteData(), new ActionDescriptor()), new List<IFilterMetadata>());
    }

    private static int? StatusOf(AuthorizationFilterContext context) => (context.Result as ObjectResult)?.StatusCode;

    [Fact]
    public void ValidKey_IsAccepted()
    {
        var context = Context(h => h.Request.Headers[PublicApiOptions.HeaderName] = "secret-1");
        Filter("secret-1").OnAuthorization(context);
        Assert.Null(context.Result);
    }

    [Fact]
    public void AnyConfiguredKey_IsAccepted()
    {
        var context = Context(h => h.Request.Headers[PublicApiOptions.HeaderName] = "secret-2");
        Filter("secret-1", "secret-2").OnAuthorization(context);
        Assert.Null(context.Result);
    }

    [Fact]
    public void MissingKey_Gets401WithAChallenge()
    {
        var context = Context();
        Filter("secret-1").OnAuthorization(context);
        Assert.Equal(401, StatusOf(context));
        Assert.Equal("ApiKey", context.HttpContext.Response.Headers.WWWAuthenticate.ToString());
    }

    [Fact]
    public void WrongKey_Gets401()
    {
        foreach (var wrong in new[] { "secret-", "secret-12", "SECRET-1", "x" })
        {
            var context = Context(h => h.Request.Headers[PublicApiOptions.HeaderName] = wrong);
            Filter("secret-1").OnAuthorization(context);
            Assert.Equal(401, StatusOf(context));
        }
    }

    [Fact]
    public void KeyInTheQueryString_IsNotAccepted()
    {
        var context = Context(h => h.Request.QueryString = new QueryString("?apikey=secret-1&key=secret-1&X-Api-Key=secret-1"));
        Filter("secret-1").OnAuthorization(context);
        Assert.Equal(401, StatusOf(context));
    }

    [Fact]
    public void NoConfiguredKeys_SwitchesTheApiOff_EvenForAnEmptyKey()
    {
        var context = Context(h => h.Request.Headers[PublicApiOptions.HeaderName] = "");
        Filter().OnAuthorization(context);
        Assert.Equal(503, StatusOf(context));

        var blankConfigured = Context(h => h.Request.Headers[PublicApiOptions.HeaderName] = " ");
        Filter("", " ").OnAuthorization(blankConfigured);
        Assert.Equal(503, StatusOf(blankConfigured));
    }
}
