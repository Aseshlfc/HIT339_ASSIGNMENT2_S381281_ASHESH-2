using LibraryManagementSystem.Data;
using LibraryManagementSystem.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LibraryManagementSystem.Tests;

/// <summary>
/// Builds a minimal, isolated DI container that mirrors the AddIdentity() setup
/// in Program.cs, so StaffController tests can exercise the real
/// UserManager/RoleManager pipeline (password hashing, role assignment,
/// lockout, etc.) against an EF Core InMemory-backed LibraryContext.
/// </summary>
internal sealed class IdentityTestHost : IDisposable
{
    private readonly ServiceProvider _provider;
    private readonly IServiceScope _scope;

    public LibraryContext Db { get; }
    public UserManager<ApplicationUser> UserManager { get; }
    public RoleManager<IdentityRole> RoleManager { get; }

    private IdentityTestHost(string dbName)
    {
        var services = new ServiceCollection();
        services.AddDbContext<LibraryContext>(options => options.UseInMemoryDatabase(dbName));
        services.AddLogging();
        services.AddIdentity<ApplicationUser, IdentityRole>()
            .AddEntityFrameworkStores<LibraryContext>()
            .AddDefaultTokenProviders();

        _provider = services.BuildServiceProvider();
        _scope = _provider.CreateScope();

        Db = _scope.ServiceProvider.GetRequiredService<LibraryContext>();
        UserManager = _scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        RoleManager = _scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
    }

    /// <summary>
    /// Creates a host with all three staff roles (Admin/Reception/Manager)
    /// already seeded, mirroring what DbSeeder does at application startup —
    /// so tests that change a user's role between any of the three don't need
    /// to seed roles themselves.
    /// </summary>
    public static async Task<IdentityTestHost> CreateAsync(string? dbName = null)
    {
        var host = new IdentityTestHost(dbName ?? Guid.NewGuid().ToString());
        foreach (var role in StaffRoles.All)
        {
            if (!await host.RoleManager.RoleExistsAsync(role))
                await host.RoleManager.CreateAsync(new IdentityRole(role));
        }
        return host;
    }

    public async Task<ApplicationUser> CreateUserAsync(string userName, string password, string role)
    {
        if (!await RoleManager.RoleExistsAsync(role))
            await RoleManager.CreateAsync(new IdentityRole(role));

        var user = new ApplicationUser { UserName = userName };
        await UserManager.CreateAsync(user, password);
        await UserManager.AddToRoleAsync(user, role);
        return user;
    }

    public void Dispose()
    {
        _scope.Dispose();
        _provider.Dispose();
    }
}
