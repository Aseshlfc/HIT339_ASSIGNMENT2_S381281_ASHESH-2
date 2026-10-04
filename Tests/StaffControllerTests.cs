using LibraryManagementSystem.Controllers;
using LibraryManagementSystem.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace LibraryManagementSystem.Tests;

public class StaffControllerTests
{
    [Fact]
    public async Task Create_ValidData_CreatesUserWithRole()
    {
        using var host = await IdentityTestHost.CreateAsync();
        var controller = new StaffController(host.UserManager);
        ControllerTestHelpers.AttachContext(controller);

        var result = await controller.Create(new StaffFormViewModel
        {
            UserName = "newreception",
            Role = "Reception",
            Password = "Password123!"
        });

        Assert.IsType<RedirectToActionResult>(result);
        var user = await host.UserManager.FindByNameAsync("newreception");
        Assert.NotNull(user);
        var roles = await host.UserManager.GetRolesAsync(user!);
        Assert.Contains("Reception", roles);
    }

    [Fact]
    public async Task Create_MissingPassword_FailsAndCreatesNoUser()
    {
        using var host = await IdentityTestHost.CreateAsync();
        var controller = new StaffController(host.UserManager);
        ControllerTestHelpers.AttachContext(controller);

        await controller.Create(new StaffFormViewModel { UserName = "nopassword", Role = "Reception", Password = null });

        Assert.False(controller.ModelState.IsValid);
        Assert.Null(await host.UserManager.FindByNameAsync("nopassword"));
    }

    [Fact]
    public async Task Create_InvalidRole_FailsValidation()
    {
        using var host = await IdentityTestHost.CreateAsync();
        var controller = new StaffController(host.UserManager);
        ControllerTestHelpers.AttachContext(controller);

        await controller.Create(new StaffFormViewModel { UserName = "badrole", Role = "SuperAdmin", Password = "Password123!" });

        Assert.False(controller.ModelState.IsValid);
        Assert.Null(await host.UserManager.FindByNameAsync("badrole"));
    }

    [Fact]
    public async Task Edit_ChangeRole_UpdatesRoleCorrectly()
    {
        using var host = await IdentityTestHost.CreateAsync();
        await host.CreateUserAsync("admin1", "Password123!", "Admin");
        await host.CreateUserAsync("admin2", "Password123!", "Admin");
        var target = await host.CreateUserAsync("staff1", "Password123!", "Reception");

        var controller = new StaffController(host.UserManager);
        ControllerTestHelpers.AttachContext(controller);

        var result = await controller.Edit(new StaffFormViewModel { Id = target.Id, UserName = "staff1", Role = "Manager" });

        Assert.IsType<RedirectToActionResult>(result);
        var roles = await host.UserManager.GetRolesAsync(target);
        Assert.Contains("Manager", roles);
        Assert.DoesNotContain("Reception", roles);
    }

    [Fact]
    public async Task Edit_LastAdmin_CannotChangeRoleAway()
    {
        using var host = await IdentityTestHost.CreateAsync();
        var onlyAdmin = await host.CreateUserAsync("soleadmin", "Password123!", "Admin");

        var controller = new StaffController(host.UserManager);
        ControllerTestHelpers.AttachContext(controller);

        await controller.Edit(new StaffFormViewModel { Id = onlyAdmin.Id, UserName = "soleadmin", Role = "Reception" });

        Assert.False(controller.ModelState.IsValid);
        var roles = await host.UserManager.GetRolesAsync(onlyAdmin);
        Assert.Contains("Admin", roles);
    }

    [Fact]
    public async Task Edit_NonLastAdmin_CanChangeRoleAway()
    {
        using var host = await IdentityTestHost.CreateAsync();
        await host.CreateUserAsync("admin1", "Password123!", "Admin");
        var secondAdmin = await host.CreateUserAsync("admin2", "Password123!", "Admin");

        var controller = new StaffController(host.UserManager);
        ControllerTestHelpers.AttachContext(controller);

        var result = await controller.Edit(new StaffFormViewModel { Id = secondAdmin.Id, UserName = "admin2", Role = "Reception" });

        Assert.IsType<RedirectToActionResult>(result);
        var roles = await host.UserManager.GetRolesAsync(secondAdmin);
        Assert.Contains("Reception", roles);
    }

    [Fact]
    public async Task ToggleLock_DisablesNonAdminAccount()
    {
        using var host = await IdentityTestHost.CreateAsync();
        var admin = await host.CreateUserAsync("admin1", "Password123!", "Admin");
        var staff = await host.CreateUserAsync("staff1", "Password123!", "Reception");

        var controller = new StaffController(host.UserManager);
        ControllerTestHelpers.AttachContext(controller, currentUserId: admin.Id);

        await controller.ToggleLock(staff.Id);

        Assert.True(await host.UserManager.IsLockedOutAsync(staff));
    }

    [Fact]
    public async Task ToggleLock_CannotDisableLastAdmin()
    {
        using var host = await IdentityTestHost.CreateAsync();
        var onlyAdmin = await host.CreateUserAsync("soleadmin", "Password123!", "Admin");
        var otherStaff = await host.CreateUserAsync("staff1", "Password123!", "Reception");

        var controller = new StaffController(host.UserManager);
        ControllerTestHelpers.AttachContext(controller, currentUserId: otherStaff.Id);

        await controller.ToggleLock(onlyAdmin.Id);

        Assert.False(await host.UserManager.IsLockedOutAsync(onlyAdmin));
    }

    [Fact]
    public async Task ToggleLock_CannotDisableOwnAccount()
    {
        using var host = await IdentityTestHost.CreateAsync();
        await host.CreateUserAsync("admin1", "Password123!", "Admin");
        var admin2 = await host.CreateUserAsync("admin2", "Password123!", "Admin");

        var controller = new StaffController(host.UserManager);
        ControllerTestHelpers.AttachContext(controller, currentUserId: admin2.Id);

        await controller.ToggleLock(admin2.Id);

        Assert.False(await host.UserManager.IsLockedOutAsync(admin2));
    }

    [Fact]
    public async Task Delete_CannotDeleteLastAdmin()
    {
        using var host = await IdentityTestHost.CreateAsync();
        var onlyAdmin = await host.CreateUserAsync("soleadmin", "Password123!", "Admin");
        var otherStaff = await host.CreateUserAsync("staff1", "Password123!", "Reception");

        var controller = new StaffController(host.UserManager);
        ControllerTestHelpers.AttachContext(controller, currentUserId: otherStaff.Id);

        await controller.DeleteConfirmed(onlyAdmin.Id);

        Assert.NotNull(await host.UserManager.FindByIdAsync(onlyAdmin.Id));
    }

    [Fact]
    public async Task Delete_NonLastAdmin_Succeeds()
    {
        using var host = await IdentityTestHost.CreateAsync();
        var admin1 = await host.CreateUserAsync("admin1", "Password123!", "Admin");
        var staff = await host.CreateUserAsync("staff1", "Password123!", "Reception");

        var controller = new StaffController(host.UserManager);
        ControllerTestHelpers.AttachContext(controller, currentUserId: admin1.Id);

        await controller.DeleteConfirmed(staff.Id);

        Assert.Null(await host.UserManager.FindByIdAsync(staff.Id));
    }

    [Fact]
    public async Task Delete_CannotDeleteOwnAccount()
    {
        using var host = await IdentityTestHost.CreateAsync();
        await host.CreateUserAsync("admin1", "Password123!", "Admin");
        var admin2 = await host.CreateUserAsync("admin2", "Password123!", "Admin");

        var controller = new StaffController(host.UserManager);
        ControllerTestHelpers.AttachContext(controller, currentUserId: admin2.Id);

        await controller.DeleteConfirmed(admin2.Id);

        Assert.NotNull(await host.UserManager.FindByIdAsync(admin2.Id));
    }
}
