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
    private static KioskController Create(LibraryContext db) => new KioskBrowser(db).Controller();

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
    public async Task SignIn_WithAnUnknownCard_GoesBackToTheWelcomeScreenWithAMessage()
    {
        using var testDb = new SqliteTestDb();
        using var db = testDb.Open();
        var browser = new KioskBrowser(db);
        var token = await browser.WelcomeTokenAsync();

        var view = await browser.Send<ViewResult>(c => c.OpenAccount("CEN", new KioskCardForm { CardNumber = "P9999", Pin = KioskBrowser.TestPin, SignInToken = token }));

        Assert.Equal("Welcome", view.ViewName);
        var model = Assert.IsType<KioskWelcomeViewModel>(view.Model);
        Assert.Contains("do not match", model.Error);
        Assert.NotEqual(token, model.SignInToken);              // a fresh form for the next try
        Assert.Null(browser.SessionCookie);
    }

    [Fact]
    public async Task SignIn_WithAValidCard_RedirectsToTheAccountPage_WhichShowsThePatron()
    {
        using var testDb = new SqliteTestDb();
        using var db = testDb.Open();
        var borrower = await Section1.AddBorrowerAsync(db, "Alice Nguyen");
        var browser = new KioskBrowser(db);
        await browser.GivePinAsync(borrower);
        var token = await browser.WelcomeTokenAsync();

        var redirect = await browser.Send<RedirectToActionResult>(c => c.OpenAccount("CEN", new KioskCardForm { CardNumber = borrower.CardNumber, Pin = KioskBrowser.TestPin, SignInToken = token }));
        Assert.Equal("Account", redirect.ActionName);

        var view = await browser.Send<ViewResult>(c => c.Account("CEN"));
        var model = Assert.IsType<KioskAccountViewModel>(view.Model);
        Assert.Equal("Alice Nguyen", model.Account.Patron.FullName);
        Assert.Null(model.Message);
    }

    [Fact]
    public async Task Checkout_ShowsSuccessOrFailureOnTheAccountScreen_Once()
    {
        using var testDb = new SqliteTestDb();
        using var db = testDb.Open();
        var (central, _, _) = await Section1.SeedBranchesAsync(db);
        await Section1.AddBookAsync(db, "B100", central, name: "Kiosk Book");
        var borrower = await Section1.AddBorrowerAsync(db);
        var browser = new KioskBrowser(db);
        await browser.SignInAsync(borrower);

        var redirect = await browser.Send<RedirectToActionResult>(c => c.Checkout("CEN", new KioskCheckoutForm { LibraryCode = "B100" }));
        Assert.Equal("Account", redirect.ActionName);
        var ok = await browser.AccountAsync();
        Assert.False(ok.MessageIsError);
        Assert.Contains("Kiosk Book", ok.Message);
        Assert.Single(ok.Account.ActiveLoans);
        Assert.Null(ok.LibraryCode);

        // Refreshing the account page does not repeat the message.
        Assert.Null((await browser.AccountAsync()).Message);

        await browser.Send<RedirectToActionResult>(c => c.Checkout("CEN", new KioskCheckoutForm { LibraryCode = "NOPE" }));
        var failed = await browser.AccountAsync();
        Assert.True(failed.MessageIsError);
        Assert.Equal("NOPE", failed.LibraryCode);          // kept so the patron can correct it
        Assert.Single(failed.Account.ActiveLoans);
    }

    [Fact]
    public async Task Checkout_WithoutASession_SendsThePatronBackToTheWelcomeScreen_AndLendsNothing()
    {
        using var testDb = new SqliteTestDb();
        using var db = testDb.Open();
        var (central, _, _) = await Section1.SeedBranchesAsync(db);
        await Section1.AddBookAsync(db, "B100", central);
        var browser = new KioskBrowser(db);

        var redirect = await browser.Send<RedirectToActionResult>(c => c.Checkout("CEN", new KioskCheckoutForm { LibraryCode = "B100" }));

        Assert.Equal("Welcome", redirect.ActionName);
        Assert.Equal(true, redirect.RouteValues!["ended"]);
        Assert.Equal(0, await db.Loans.CountAsync());
    }

    [Fact]
    public async Task EveryKioskPostAction_RequiresAnAntiForgeryToken()
    {
        foreach (var name in new[] { nameof(KioskController.OpenAccount), nameof(KioskController.Checkout), nameof(KioskController.Finish) })
        {
            var method = typeof(KioskController).GetMethod(name)!;
            Assert.NotNull(method.GetCustomAttributes(typeof(HttpPostAttribute), false).SingleOrDefault());
            Assert.NotEmpty(method.GetCustomAttributes(typeof(ValidateAntiForgeryTokenAttribute), false));
        }
        await Task.CompletedTask;
    }

    // Regression for Windows testing feedback: after signing out there was no way back to the main site.
    [Fact]
    public async Task Welcome_AfterSignOut_ShowsTheSignedOutScreenWithNoPatronDetails()
    {
        using var testDb = new SqliteTestDb();
        using var db = testDb.Open();
        var borrower = await Section1.AddBorrowerAsync(db);
        var browser = new KioskBrowser(db);
        await browser.SignInAsync(borrower);

        var redirect = await browser.Send<RedirectToActionResult>(c => c.Finish("CEN"));
        Assert.Equal("Welcome", redirect.ActionName);
        Assert.Equal(true, redirect.RouteValues!["signedOut"]);

        var view = await browser.Send<ViewResult>(c => c.Welcome("CEN", signedOut: true));
        var model = Assert.IsType<KioskWelcomeViewModel>(view.Model);
        Assert.True(model.SignedOut);
        Assert.Null(model.CardNumber);
        Assert.Null(model.Error);

        var normal = Assert.IsType<KioskWelcomeViewModel>((await browser.Send<ViewResult>(c => c.Welcome("CEN"))).Model);
        Assert.False(normal.SignedOut);
    }

    [Fact]
    public async Task SignOut_GoesToTheSignedOutScreen_WhichOffersTheWayBackToTheMainSite()
    {
        var welcome = await File.ReadAllTextAsync(ViewPath("Welcome.cshtml"));
        var account = await File.ReadAllTextAsync(ViewPath("Account.cshtml"));
        var exitLink = await File.ReadAllTextAsync(ViewPath("_BackToMainSite.cshtml"));

        Assert.Contains("Back to Main Site", exitLink);
        Assert.Contains("Url.Action(\"Index\", \"Home\")", exitLink);
        Assert.Contains("name=\"SignInToken\"", welcome);

        // Finish and the idle timer both post the sign-out form, so the session ends on the server.
        Assert.Contains("asp-action=\"Finish\"", account);
        Assert.Contains("finish.submit()", account);

        // The way out is only offered once nobody is signed in, and the card number
        // is never sent back from the account page.
        Assert.DoesNotContain("Back to Main Site", account);
        Assert.DoesNotContain("_BackToMainSite", account);
        Assert.DoesNotContain("name=\"CardNumber\"", account);
    }

    // Regression: the branch-choice screen had no way back to the main site.
    [Theory]
    [InlineData("Index.cshtml")]          // choose a branch
    [InlineData("Welcome.cshtml")]        // before sign-in and after sign-out
    [InlineData("BranchNotFound.cshtml")]
    public async Task EveryPublicKioskScreen_OffersTheWayBackToTheMainSite(string view)
    {
        var markup = await File.ReadAllTextAsync(ViewPath(view));

        Assert.Contains("<partial name=\"_BackToMainSite\" />", markup);
    }

    // Regression: on phones the staff menu's <nav> styles (right padding, fade-out mask)
    // also hit the kiosk's exit landmark, squeezing and fading the button.
    [Fact]
    public async Task TheExitLandmark_DoesNotInheritTheStaffMenusPhoneStyles()
    {
        var dir = Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(ViewPath("Index.cshtml"))))!;
        var css = await File.ReadAllTextAsync(Path.Combine(dir, "wwwroot", "css", "kiosk.css"));

        var reset = System.Text.RegularExpressions.Regex.Match(css, @"\.kiosk-exit \{ flex: none;[^}]*\}").Value;
        Assert.Contains("padding-right: 0", reset);
        Assert.Contains("mask-image: none", reset);
        Assert.Contains("overflow: visible", reset);
    }

    [Fact]
    public async Task TheBackToMainSiteLink_IsALabelledLandmarkWithADecorativeIcon()
    {
        var exitLink = await File.ReadAllTextAsync(ViewPath("_BackToMainSite.cshtml"));

        Assert.Contains("<nav class=\"kiosk-exit\" aria-label=\"Leave the kiosk\">", exitLink);
        Assert.Contains("<i class=\"bi bi-arrow-left\" aria-hidden=\"true\"></i> Back to Main Site", exitLink);
    }

    [Fact]
    public async Task KioskScreens_AreNeverStoredByTheBrowser()
    {
        foreach (var name in new[] { nameof(KioskController.Welcome), nameof(KioskController.OpenAccount), nameof(KioskController.Account), nameof(KioskController.Checkout) })
        {
            var cache = typeof(KioskController).GetMethod(name)!
                .GetCustomAttributes(typeof(ResponseCacheAttribute), false)
                .Cast<ResponseCacheAttribute>()
                .SingleOrDefault();
            Assert.NotNull(cache);
            Assert.True(cache!.NoStore);
            Assert.Equal(ResponseCacheLocation.None, cache.Location);
        }
        await Task.CompletedTask;
    }

    private static string ViewPath(string file)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "LibraryManagementSystem.csproj")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "Views", "Kiosk", file);
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
