using System.Security.Claims;
using LibraryManagementSystem.Data;
using LibraryManagementSystem.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;

namespace LibraryManagementSystem.Tests;

/// <summary>Shared helpers for the multi-branch, transfer and reporting tests.</summary>
internal static class Section1
{
    /// <summary>Signs a named user with the given roles in to a controller.</summary>
    public static void SignIn(Controller controller, string userName, params string[] roles)
    {
        var claims = new List<Claim> { new(ClaimTypes.Name, userName), new(ClaimTypes.NameIdentifier, userName) };
        claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));
        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"))
        };
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
        controller.TempData = new TempDataDictionary(httpContext, new NoOpTempDataProvider());
    }

    /// <summary>Adds two extra branches next to the default one and returns all three.</summary>
    public static async Task<(Branch central, Branch north, Branch river)> SeedBranchesAsync(LibraryContext db)
    {
        var central = await db.Branches.FindAsync(Branch.DefaultBranchId);
        if (central == null)
        {
            central = new Branch { Id = Branch.DefaultBranchId, Code = "CEN", Name = "Central Library", Address = "1 Library Square" };
            db.Branches.Add(central);
            // Saved on its own so the provider's key generator moves past Id 1.
            await db.SaveChangesAsync();
        }
        var north = new Branch { Code = "NTH", Name = "Northside Branch", Address = "88 Northside Parade" };
        var river = new Branch { Code = "RIV", Name = "Riverside Branch", Address = "14 Riverside Drive" };
        db.Branches.AddRange(north, river);
        await db.SaveChangesAsync();
        return (central, north, river);
    }

    public static async Task<Book> AddBookAsync(LibraryContext db, string code, Branch branch, ItemStatus status = ItemStatus.Available, string? name = null)
    {
        var book = new Book { Name = name ?? $"Book {code}", Description = "d", LibraryCode = code, Status = status, Author = "A", Genre = "G", BranchId = branch.Id };
        db.Items.Add(book);
        await db.SaveChangesAsync();
        return book;
    }

    public static async Task<Borrower> AddBorrowerAsync(LibraryContext db, string name = "Test Borrower")
    {
        var borrower = new Borrower { FullName = name, Email = "t@example.com", Phone = "0400000000", Address = "1 Test St" };
        db.Borrowers.Add(borrower);
        await db.SaveChangesAsync();
        return borrower;
    }

    /// <summary>Creates a staff account row assigned to a branch (no password needed for these tests).</summary>
    public static async Task AddStaffAsync(LibraryContext db, string userName, Branch? branch)
    {
        db.Users.Add(new ApplicationUser { UserName = userName, NormalizedUserName = userName.ToUpperInvariant(), BranchId = branch?.Id });
        await db.SaveChangesAsync();
    }
}

/// <summary>
/// A real SQLite database in a temporary file, created from the EF model
/// (which also inserts the default branch). Used where the InMemory provider
/// would hide the behaviour under test: concurrency tokens, unique indexes
/// and decimal values stored as TEXT.
/// </summary>
internal sealed class SqliteTestDb : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"section1-test-{Guid.NewGuid():N}.db");

    public SqliteTestDb()
    {
        using var db = Open();
        db.Database.EnsureCreated();
    }

    public LibraryContext Open()
        => new(new DbContextOptionsBuilder<LibraryContext>().UseSqlite($"Data Source={_path}").Options);

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (File.Exists(_path)) File.Delete(_path);
    }
}
