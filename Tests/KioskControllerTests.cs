using System.Text;
using LibraryManagementSystem.Controllers;
using LibraryManagementSystem.Data;
using LibraryManagementSystem.Models;
using LibraryManagementSystem.Services;
using LibraryManagementSystem.ViewModels;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LibraryManagementSystem.Tests;

public class KioskControllerTests
{
    private static KioskController Create(LibraryContext db)
    {
        var kiosk = new KioskService(db, new NotificationService(db, NullLogger<NotificationService>.Instance));
        var controller = new KioskController(db, kiosk);
        ControllerTestHelpers.AttachContext(controller);
        return controller;
    }

    [Fact]
    public async Task Index_SkipsTheBranchPickerWhenThereIsOnlyOneActiveBranch()
    {
        using var testDb = new SqliteTestDb();
        using var db = testDb.Open();

        var result = await Create(db).Index();

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Welcome", redirect.ActionName);
        Assert.Equal("CEN", redirect.RouteValues!["branchCode"]);
    }

    [Fact]
    public async Task Index_OffersEveryActiveBranchWhenThereAreSeveral()
    {
        using var testDb = new SqliteTestDb();
        using var db = testDb.Open();
        var (_, north, river) = await Section1.SeedBranchesAsync(db);
        river.IsActive = false;
        await db.SaveChangesAsync();

        var view = Assert.IsType<ViewResult>(await Create(db).Index());

        var branches = Assert.IsType<List<Branch>>(view.Model);
        Assert.Equal(2, branches.Count);
        Assert.DoesNotContain(branches, b => b.Id == river.Id);
    }

    [Fact]
    public async Task UnknownOrInactiveBranch_Gets404()
    {
        using var testDb = new SqliteTestDb();
        using var db = testDb.Open();
        var (_, north, _) = await Section1.SeedBranchesAsync(db);
        north.IsActive = false;
        await db.SaveChangesAsync();
        var controller = Create(db);

        var unknown = Assert.IsType<ViewResult>(await controller.Welcome("ZZZ"));
        Assert.Equal("BranchNotFound", unknown.ViewName);
        Assert.Equal(404, controller.Response.StatusCode);

        var inactive = Assert.IsType<ViewResult>(await Create(db).Welcome("NTH"));
        Assert.Equal("BranchNotFound", inactive.ViewName);
    }

    [Fact]
    public async Task Welcome_AcceptsTheBranchCodeInAnyCase()
    {
        using var testDb = new SqliteTestDb();
        using var db = testDb.Open();

        var view = Assert.IsType<ViewResult>(await Create(db).Welcome("cen"));

        Assert.Equal("CEN", Assert.IsType<KioskWelcomeViewModel>(view.Model).Branch.Code);
    }

    [Fact]
    public async Task Account_WithAnUnknownCard_GoesBackToTheWelcomeScreenWithAMessage()
    {
        using var testDb = new SqliteTestDb();
        using var db = testDb.Open();

        var view = Assert.IsType<ViewResult>(await Create(db).Account("CEN", new KioskCardForm { CardNumber = "P9999" }));

        Assert.Equal("Welcome", view.ViewName);
        Assert.Contains("not recognised", Assert.IsType<KioskWelcomeViewModel>(view.Model).Error);
    }

    [Fact]
    public async Task Account_WithAValidCard_ShowsThePatronsAccount()
    {
        using var testDb = new SqliteTestDb();
        using var db = testDb.Open();
        var borrower = await Section1.AddBorrowerAsync(db, "Alice Nguyen");

        var view = Assert.IsType<ViewResult>(await Create(db).Account("CEN", new KioskCardForm { CardNumber = borrower.CardNumber }));

        Assert.Equal("Account", view.ViewName);
        var model = Assert.IsType<KioskAccountViewModel>(view.Model);
        Assert.Equal("Alice Nguyen", model.Account.Patron.FullName);
        Assert.Null(model.Message);
    }

    [Fact]
    public async Task Checkout_ShowsSuccessOrFailureOnTheAccountScreen()
    {
        using var testDb = new SqliteTestDb();
        using var db = testDb.Open();
        var (central, _, _) = await Section1.SeedBranchesAsync(db);
        await Section1.AddBookAsync(db, "B100", central, name: "Kiosk Book");
        var borrower = await Section1.AddBorrowerAsync(db);

        var ok = Assert.IsType<KioskAccountViewModel>(Assert.IsType<ViewResult>(
            await Create(db).Checkout("CEN", new KioskCheckoutForm { CardNumber = borrower.CardNumber, LibraryCode = "B100" })).Model);
        Assert.False(ok.MessageIsError);
        Assert.Contains("Kiosk Book", ok.Message);
        Assert.Single(ok.Account.ActiveLoans);
        Assert.Null(ok.LibraryCode);

        var failed = Assert.IsType<KioskAccountViewModel>(Assert.IsType<ViewResult>(
            await Create(db).Checkout("CEN", new KioskCheckoutForm { CardNumber = borrower.CardNumber, LibraryCode = "NOPE" })).Model);
        Assert.True(failed.MessageIsError);
        Assert.Equal("NOPE", failed.LibraryCode);          // kept so the patron can correct it
        Assert.Single(failed.Account.ActiveLoans);
    }

    [Fact]
    public async Task Checkout_WithAnUnknownCard_SendsThePatronBackToTheWelcomeScreen_AndLendsNothing()
    {
        using var testDb = new SqliteTestDb();
        using var db = testDb.Open();
        var (central, _, _) = await Section1.SeedBranchesAsync(db);
        await Section1.AddBookAsync(db, "B100", central);

        var view = Assert.IsType<ViewResult>(
            await Create(db).Checkout("CEN", new KioskCheckoutForm { CardNumber = "P9999", LibraryCode = "B100" }));

        Assert.Equal("Welcome", view.ViewName);
        Assert.Equal(0, await db.Loans.CountAsync());
    }

    [Fact]
    public async Task EveryKioskPostAction_RequiresAnAntiForgeryToken()
    {
        foreach (var name in new[] { nameof(KioskController.Account), nameof(KioskController.Checkout) })
        {
            var method = typeof(KioskController).GetMethod(name)!;
            Assert.NotEmpty(method.GetCustomAttributes(typeof(ValidateAntiForgeryTokenAttribute), false));
        }
        await Task.CompletedTask;
    }
}

public class ImportControllerTests
{
    private static ImportController Create(LibraryContext db)
    {
        var controller = new ImportController(new ItemImportService(db));
        Section1.SignIn(controller, "admin", "Admin");
        return controller;
    }

    private static IFormFile CsvFile(string name, string content)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        return new FormFile(new MemoryStream(bytes), 0, bytes.Length, "csvFile", name);
    }

    [Fact]
    public async Task UploadingNothingOrANonCsvFile_ShowsAFormErrorAndImportsNothing()
    {
        using var testDb = new SqliteTestDb();
        using var db = testDb.Open();
        var controller = Create(db);

        var none = Assert.IsType<ViewResult>(await controller.Index(null));
        Assert.False(controller.ModelState.IsValid);
        Assert.Null(Assert.IsType<ImportPageViewModel>(none.Model).Report);

        var controller2 = Create(db);
        await controller2.Index(CsvFile("items.txt", "Type,Name\r\n"));
        Assert.False(controller2.ModelState.IsValid);
        Assert.Equal(0, await db.Items.CountAsync());
    }

    [Fact]
    public async Task UploadingACsv_ReturnsTheReportOnTheSamePage()
    {
        using var testDb = new SqliteTestDb();
        using var db = testDb.Open();
        var csv = new ItemImportService(db).TemplateCsv();

        var view = Assert.IsType<ViewResult>(await Create(db).Index(CsvFile("items.CSV", csv), validateOnly: false));

        var model = Assert.IsType<ImportPageViewModel>(view.Model);
        Assert.Equal(3, model.Report!.ImportedCount);
        Assert.Equal("items.CSV", model.FileName);
    }

    [Fact]
    public void ImportIsAdminOnly_AndTheTemplateIsDownloadable()
    {
        var authorize = typeof(ImportController).GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AuthorizeAttribute), true)
            .Cast<Microsoft.AspNetCore.Authorization.AuthorizeAttribute>().Single();
        Assert.Equal("Admin", authorize.Roles);

        using var testDb = new SqliteTestDb();
        using var db = testDb.Open();
        var file = Assert.IsType<FileContentResult>(Create(db).Template());
        Assert.Equal("text/csv", file.ContentType);
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, file.FileContents.Take(3).ToArray());   // UTF-8 BOM for Excel
    }
}
