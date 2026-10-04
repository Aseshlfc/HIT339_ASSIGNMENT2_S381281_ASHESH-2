using LibraryManagementSystem.Data;
using Microsoft.EntityFrameworkCore;

namespace LibraryManagementSystem.Tests;

internal static class TestDb
{
    public static LibraryContext Create() => Create(Guid.NewGuid().ToString());

    /// <summary>
    /// Creates a new LibraryContext instance pointed at the named InMemory
    /// database. Used to simulate two separate HTTP requests (each with its
    /// own scoped DbContext) operating against the same underlying data,
    /// e.g. for concurrency tests.
    /// </summary>
    public static LibraryContext Create(string dbName)
    {
        var options = new DbContextOptionsBuilder<LibraryContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        return new LibraryContext(options);
    }
}
