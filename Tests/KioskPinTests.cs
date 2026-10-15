using System.Reflection;
using LibraryManagementSystem.Controllers;
using LibraryManagementSystem.Data;
using LibraryManagementSystem.Models;
using LibraryManagementSystem.Services;
using LibraryManagementSystem.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace LibraryManagementSystem.Tests;

/// <summary>
/// Kiosk PINs: format, hashing, lockout, staff management and the effect on kiosk sessions.
/// The PINs here exist only in tests; the application ships with no demo PINs.
/// </summary>
public class KioskPinTests
{
    private const string Pin = KioskBrowser.TestPin;      // "024680"
    private const string OtherPin = "135790";

    // ---------- Format and storage

    [Theory]
    [InlineData("000000")]
    [InlineData("012345")]
    [InlineData("999999")]
    public void SixAsciiDigits_AreValidPins(string pin) => Assert.True(KioskPinService.IsValidPin(pin));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("12345")]
    [InlineData("1234567")]
    [InlineData("12a456")]
    [InlineData(" 12345")]
    [InlineData("12345 ")]
    [InlineData("１２３４５６")]          // full-width digits
    [InlineData("١٢٣٤٥٦")]          // Arabic-Indic digits
    public void AnythingElse_IsNotAValidPin(string? pin) => Assert.False(KioskPinService.IsValidPin(pin));

    [Fact]
    public async Task SetPin_StoresOnlyASaltedHash()
    {
        using var testDb = new SqliteTestDb();
        using var db = testDb.Open();
        var alice = await Section1.AddBorrowerAsync(db, "Alice");
        var bob = await Section1.AddBorrowerAsync(db, "Bob");
        var browser = new KioskBrowser(db);

        await browser.GivePinAsync(alice);
        await browser.GivePinAsync(bob);
        db.ChangeTracker.Clear();
        var a = await db.Borrowers.SingleAsync(b => b.Id == alice.Id);
        var b2 = await db.Borrowers.SingleAsync(b => b.Id == bob.Id);

        Assert.True(a.HasKioskPin);
        Assert.NotNull(a.KioskPinSetAtUtc);
        Assert.DoesNotContain(Pin, a.KioskPinHash!);
        Assert.NotEqual(a.KioskPinHash, b2.KioskPinHash);          // same PIN, different salt
        Assert.True(a.KioskPinHash!.Length > 40);
    }

    [Fact]
    public async Task SetPin_RefusesABadPin_AndUnknownBorrowers()
    {
        using var testDb = new SqliteTestDb();
        using var db = testDb.Open();
        var patron = await Section1.AddBorrowerAsync(db);
        var browser = new KioskBrowser(db);

        await Assert.ThrowsAsync<ArgumentException>(() => browser.Pins.SetPinAsync(patron.Id, "12345"));
        Assert.False(await browser.Pins.SetPinAsync(99999, Pin));
        Assert.Null((await db.Borrowers.AsNoTracking().SingleAsync(b => b.Id == patron.Id)).KioskPinHash);
    }

    [Fact]
    public async Task LeadingZeros_ArePartOfThePin()
    {
        using var testDb = new SqliteTestDb();
        using var db = testDb.Open();
        var patron = await Section1.AddBorrowerAsync(db);
        var browser = new KioskBrowser(db);
        await browser.GivePinAsync(patron, "012345");

        Assert.Equal(KioskSignInStatus.MissingPin, (await browser.Pins.SignInAsync(patron.CardNumber, "12345")).Status);
        Assert.Equal(KioskSignInStatus.InvalidCredentials, (await browser.Pins.SignInAsync(patron.CardNumber, "123450")).Status);
        Assert.Equal(KioskSignInStatus.Success, (await browser.Pins.SignInAsync(patron.CardNumber, "012345")).Status);
    }

    // ---------- Signing in

    [Fact]
    public async Task SignIn_ReportsEachOutcome()
    {
        using var testDb = new SqliteTestDb();
        using var db = testDb.Open();
        var patron = await Section1.AddBorrowerAsync(db);
        var pins = new KioskBrowser(db).Pins;

        Assert.Equal(KioskSignInStatus.MissingCard, (await pins.SignInAsync(" ", Pin)).Status);
        Assert.Equal(KioskSignInStatus.MissingPin, (await pins.SignInAsync(patron.CardNumber, "")).Status);
        Assert.Equal(KioskSignInStatus.NoPinSet, (await pins.SignInAsync(patron.CardNumber, Pin)).Status);
        Assert.Equal(KioskSignInStatus.InvalidCredentials, (await pins.SignInAsync("P9999", Pin)).Status);
        Assert.Equal(KioskSignInStatus.InvalidCredentials, (await pins.SignInAsync("not a card", Pin)).Status);

        await pins.SetPinAsync(patron.Id, Pin);
        Assert.Equal(KioskSignInStatus.InvalidCredentials, (await pins.SignInAsync(patron.CardNumber, OtherPin)).Status);
        var ok = await pins.SignInAsync(patron.CardNumber.ToLowerInvariant(), Pin);
        Assert.Equal(KioskSignInStatus.Success, ok.Status);
        Assert.Equal(patron.Id, ok.Patron!.Id);
    }

    [Fact]
    public async Task FiveWrongPins_LockTheCardFor15Minutes_EvenAgainstTheRightPin()
    {
        using var testDb = new SqliteTestDb();
        using var db = testDb.Open();
        var patron = await Section1.AddBorrowerAsync(db);
        var browser = new KioskBrowser(db);
        await browser.GivePinAsync(patron);

        for (var i = 1; i < KioskPinService.MaxFailedAttempts; i++)
            Assert.Equal(KioskSignInStatus.InvalidCredentials, (await browser.Pins.SignInAsync(patron.CardNumber, OtherPin)).Status);
        Assert.Equal(KioskSignInStatus.LockedOut, (await browser.Pins.SignInAsync(patron.CardNumber, OtherPin)).Status);

        var stored = await db.Borrowers.AsNoTracking().SingleAsync(b => b.Id == patron.Id);
        Assert.Equal(browser.Clock.Now.UtcDateTime + KioskPinService.LockoutDuration, stored.KioskPinLockedUntilUtc);

        // While locked, even the right PIN is refused, and nothing more is counted.
        browser.Clock.Advance(TimeSpan.FromMinutes(14));
        Assert.Equal(KioskSignInStatus.LockedOut, (await browser.Pins.SignInAsync(patron.CardNumber, Pin)).Status);

        browser.Clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(KioskSignInStatus.Success, (await browser.Pins.SignInAsync(patron.CardNumber, Pin)).Status);
        stored = await db.Borrowers.AsNoTracking().SingleAsync(b => b.Id == patron.Id);
        Assert.Equal(0, stored.KioskPinFailedAttempts);
        Assert.Null(stored.KioskPinLockedUntilUtc);
    }

    [Fact]
    public async Task TwentySimultaneousWrongPins_GetOnlyFiveChecks()
    {
        // Each attempt has its own database connection, like separate kiosks. Before the
        // fix every attempt was checked against the hash before any was counted, so all
        // twenty were real guesses. Now only five can get past the reservation.
        using var testDb = new SqliteTestDb();
        Borrower patron;
        var clock = new FakeClock();
        var store = new KioskSessionStore(new Microsoft.AspNetCore.DataProtection.EphemeralDataProtectionProvider(),
            Microsoft.Extensions.Options.Options.Create(new LibraryManagementSystem.Api.KioskOptions()), clock);
        using (var setup = testDb.Open())
        {
            patron = await Section1.AddBorrowerAsync(setup);
            await new KioskPinService(setup, store, clock).SetPinAsync(patron.Id, Pin);
        }

        var contexts = Enumerable.Range(0, 20).Select(_ => testDb.Open()).ToList();
        try
        {
            var results = await Task.WhenAll(contexts.Select((db, i) =>
                Task.Run(() => new KioskPinService(db, store, clock).SignInAsync(patron.CardNumber, (100000 + i).ToString()))));

            Assert.Equal(KioskPinService.MaxFailedAttempts, results.Count(r => r.PinChecked));
            Assert.DoesNotContain(results, r => r.Status == KioskSignInStatus.Success);
            Assert.All(results.Where(r => !r.PinChecked), r => Assert.Equal(KioskSignInStatus.LockedOut, r.Status));
        }
        finally
        {
            foreach (var db in contexts) db.Dispose();
        }

        using var check = testDb.Open();
        Assert.Equal(KioskSignInStatus.LockedOut, (await new KioskPinService(check, store, clock).SignInAsync(patron.CardNumber, Pin)).Status);
    }

    [Fact]
    public async Task AfterALockExpires_TheCountStartsAgain()
    {
        using var testDb = new SqliteTestDb();
        using var db = testDb.Open();
        var patron = await Section1.AddBorrowerAsync(db);
        var browser = new KioskBrowser(db);
        await browser.GivePinAsync(patron);
        for (var i = 0; i < KioskPinService.MaxFailedAttempts; i++)
            await browser.Pins.SignInAsync(patron.CardNumber, OtherPin);

        browser.Clock.Advance(KioskPinService.LockoutDuration);

        for (var i = 1; i < KioskPinService.MaxFailedAttempts; i++)
            Assert.Equal(KioskSignInStatus.InvalidCredentials, (await browser.Pins.SignInAsync(patron.CardNumber, OtherPin)).Status);
        Assert.Equal(KioskSignInStatus.LockedOut, (await browser.Pins.SignInAsync(patron.CardNumber, OtherPin)).Status);
    }

    [Fact]
    public async Task ACorrectFifthPin_StillSignsIn_AndClearsTheCount()
    {
        using var testDb = new SqliteTestDb();
        using var db = testDb.Open();
        var patron = await Section1.AddBorrowerAsync(db);
        var browser = new KioskBrowser(db);
        await browser.GivePinAsync(patron);
        for (var i = 1; i < KioskPinService.MaxFailedAttempts; i++)
            await browser.Pins.SignInAsync(patron.CardNumber, OtherPin);

        Assert.Equal(KioskSignInStatus.Success, (await browser.Pins.SignInAsync(patron.CardNumber, Pin)).Status);
        var stored = await db.Borrowers.AsNoTracking().SingleAsync(b => b.Id == patron.Id);
        Assert.Equal(0, stored.KioskPinFailedAttempts);
        Assert.Null(stored.KioskPinLockedUntilUtc);
    }

    [Fact]
    public async Task ACorrectPin_ResetsTheWrongPinCount()
    {
        using var testDb = new SqliteTestDb();
        using var db = testDb.Open();
        var patron = await Section1.AddBorrowerAsync(db);
        var browser = new KioskBrowser(db);
        await browser.GivePinAsync(patron);

        for (var i = 0; i < KioskPinService.MaxFailedAttempts - 1; i++)
            await browser.Pins.SignInAsync(patron.CardNumber, OtherPin);
        await browser.Pins.SignInAsync(patron.CardNumber, Pin);
        for (var i = 0; i < KioskPinService.MaxFailedAttempts - 1; i++)
            Assert.Equal(KioskSignInStatus.InvalidCredentials, (await browser.Pins.SignInAsync(patron.CardNumber, OtherPin)).Status);

        Assert.Equal(KioskSignInStatus.Success, (await browser.Pins.SignInAsync(patron.CardNumber, Pin)).Status);
    }

    [Fact]
    public async Task ResettingThePin_UnlocksTheCard_AndTheOldPinStopsWorking()
    {
        using var testDb = new SqliteTestDb();
        using var db = testDb.Open();
        var patron = await Section1.AddBorrowerAsync(db);
        var browser = new KioskBrowser(db);
        await browser.GivePinAsync(patron);
        for (var i = 0; i < KioskPinService.MaxFailedAttempts; i++)
            await browser.Pins.SignInAsync(patron.CardNumber, "111111");

        await browser.GivePinAsync(patron, OtherPin);

        Assert.Equal(KioskSignInStatus.InvalidCredentials, (await browser.Pins.SignInAsync(patron.CardNumber, Pin)).Status);
        Assert.Equal(KioskSignInStatus.Success, (await browser.Pins.SignInAsync(patron.CardNumber, OtherPin)).Status);
    }

    // ---------- Kiosk screens

    [Fact]
    public async Task Kiosk_WithoutAPin_TellsThePatronToAskStaff_AndStartsNoSession()
    {
        using var testDb = new SqliteTestDb();
        using var db = testDb.Open();
        var patron = await Section1.AddBorrowerAsync(db);
        var browser = new KioskBrowser(db);
        var token = await browser.WelcomeTokenAsync();

        var view = await browser.Send<ViewResult>(c => c.OpenAccount("CEN", new KioskCardForm { CardNumber = patron.CardNumber, Pin = Pin, SignInToken = token }));

        Assert.Contains("ask library staff", Assert.IsType<KioskWelcomeViewModel>(view.Model).Error);
        Assert.Null(browser.SessionCookie);
    }

    [Fact]
    public async Task Kiosk_GivesTheSameMessageForAnUnknownCardAndAWrongPin_AndNeverEchoesThem()
    {
        using var testDb = new SqliteTestDb();
        using var db = testDb.Open();
        var patron = await Section1.AddBorrowerAsync(db);
        var browser = new KioskBrowser(db);
        await browser.GivePinAsync(patron);

        async Task<KioskWelcomeViewModel> Try(string card, string pin)
        {
            var token = await browser.WelcomeTokenAsync();
            var view = await browser.Send<ViewResult>(c => c.OpenAccount("CEN", new KioskCardForm { CardNumber = card, Pin = pin, SignInToken = token }));
            return Assert.IsType<KioskWelcomeViewModel>(view.Model);
        }

        var unknownCard = await Try("P9999", Pin);
        var wrongPin = await Try(patron.CardNumber, OtherPin);

        Assert.Equal(unknownCard.Error, wrongPin.Error);
        Assert.Null(unknownCard.CardNumber);
        Assert.Null(wrongPin.CardNumber);
        Assert.Null(typeof(KioskWelcomeViewModel).GetProperty("Pin"));       // a PIN can never be sent back

        // Only a missing PIN keeps the card number, so a scanned card need not be scanned again.
        var missingPin = await Try(patron.CardNumber, "");
        Assert.Equal(patron.CardNumber, missingPin.CardNumber);
        Assert.Contains("6-digit", missingPin.Error);
    }

    [Fact]
    public async Task Kiosk_ShowsTheLockoutMessage_OnTheFifthWrongPin()
    {
        using var testDb = new SqliteTestDb();
        using var db = testDb.Open();
        var patron = await Section1.AddBorrowerAsync(db);
        var browser = new KioskBrowser(db);
        await browser.GivePinAsync(patron);

        KioskWelcomeViewModel? last = null;
        for (var i = 0; i < KioskPinService.MaxFailedAttempts; i++)
        {
            var token = await browser.WelcomeTokenAsync();
            var view = await browser.Send<ViewResult>(c => c.OpenAccount("CEN", new KioskCardForm { CardNumber = patron.CardNumber, Pin = OtherPin, SignInToken = token }));
            last = Assert.IsType<KioskWelcomeViewModel>(view.Model);
        }

        Assert.Contains("15 minutes", last!.Error);
        Assert.Null(browser.SessionCookie);
    }

    [Fact]
    public async Task ChangingThePin_EndsTheKioskSessionAlreadyOpen()
    {
        using var testDb = new SqliteTestDb();
        using var db = testDb.Open();
        var patron = await Section1.AddBorrowerAsync(db);
        var browser = new KioskBrowser(db);
        await browser.SignInAsync(patron);

        await browser.GivePinAsync(patron, OtherPin);

        Assert.True(await browser.AccountIsClosedAsync());
    }

    [Fact]
    public async Task ChangingThePinElsewhere_StillEndsTheSession()
    {
        // As if another copy of the application changed the PIN: this store is never told,
        // but the session's PIN version no longer matches the database.
        using var testDb = new SqliteTestDb();
        using var db = testDb.Open();
        var patron = await Section1.AddBorrowerAsync(db);
        var browser = new KioskBrowser(db);
        await browser.SignInAsync(patron);

        await db.Borrowers.Where(b => b.Id == patron.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(b => b.KioskPinSetAtUtc, DateTime.UtcNow.AddMinutes(1)));

        Assert.True(await browser.AccountIsClosedAsync());
    }

    [Fact]
    public async Task ALockedCard_DoesNotEndAKioskSessionAlreadyOpen()
    {
        // Otherwise anyone could throw a patron off a kiosk by typing wrong PINs elsewhere.
        using var testDb = new SqliteTestDb();
        using var db = testDb.Open();
        var patron = await Section1.AddBorrowerAsync(db);
        var browser = new KioskBrowser(db);
        await browser.SignInAsync(patron);

        for (var i = 0; i < KioskPinService.MaxFailedAttempts; i++)
            await browser.Pins.SignInAsync(patron.CardNumber, OtherPin);

        Assert.Equal(patron.Id, (await browser.AccountAsync()).Account.Patron.Id);
    }

    [Fact]
    public async Task PinBoxes_AreMaskedPasswordFields_ThatBrowsersWillNotAutofill()
    {
        foreach (var path in new[] { ViewPath("Kiosk", "Welcome.cshtml"), ViewPath("Borrowers", "KioskPin.cshtml") })
        {
            var view = await File.ReadAllTextAsync(path);
            var boxes = System.Text.RegularExpressions.Regex.Matches(view, "<input id=\"(Pin|ConfirmPin)\"[^>]*>");
            Assert.NotEmpty(boxes);
            foreach (System.Text.RegularExpressions.Match box in boxes)
            {
                Assert.Contains("type=\"password\"", box.Value);
                Assert.Contains("autocomplete=\"new-password\"", box.Value);
                Assert.Contains("inputmode=\"numeric\"", box.Value);
                Assert.DoesNotContain("value=", box.Value);
            }
        }
    }

    private static string ViewPath(string folder, string file)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "LibraryManagementSystem.csproj")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "Views", folder, file);
    }

    // ---------- Staff: Borrowers page

    private static BorrowersController Staff(LibraryContext db, KioskBrowser browser, string role = "Reception")
    {
        var controller = new BorrowersController(db, browser.Pins);
        Section1.SignIn(controller, "staff", role);
        return controller;
    }

    [Fact]
    public async Task Staff_CanSetAPin_AndTheConfirmationNeverShowsIt()
    {
        using var testDb = new SqliteTestDb();
        using var db = testDb.Open();
        var patron = await Section1.AddBorrowerAsync(db, "Alice");
        var browser = new KioskBrowser(db);
        var controller = Staff(db, browser);

        var result = await controller.KioskPin(patron.Id, new KioskPinForm { Pin = Pin, ConfirmPin = Pin });

        Assert.Equal("Index", Assert.IsType<RedirectToActionResult>(result).ActionName);
        var message = Assert.IsType<string>(controller.TempData["Success"]);
        Assert.Contains("Alice", message);
        Assert.DoesNotContain(Pin, message);
        Assert.True((await db.Borrowers.AsNoTracking().SingleAsync(b => b.Id == patron.Id)).HasKioskPin);
        Assert.Equal(KioskSignInStatus.Success, (await browser.Pins.SignInAsync(patron.CardNumber, Pin)).Status);
    }

    [Theory]
    [InlineData("12345", "12345", "Pin")]
    [InlineData("12ab56", "12ab56", "Pin")]
    [InlineData("024680", "024681", "ConfirmPin")]
    [InlineData("024680", null, "ConfirmPin")]
    public async Task Staff_GetAFormError_ForABadOrMismatchedPin(string pin, string? confirm, string field)
    {
        using var testDb = new SqliteTestDb();
        using var db = testDb.Open();
        var patron = await Section1.AddBorrowerAsync(db);
        var controller = Staff(db, new KioskBrowser(db));

        var view = Assert.IsType<ViewResult>(await controller.KioskPin(patron.Id, new KioskPinForm { Pin = pin, ConfirmPin = confirm }));

        Assert.True(controller.ModelState[field]!.Errors.Count > 0);
        Assert.IsType<KioskPinViewModel>(view.Model);
        Assert.Null((await db.Borrowers.AsNoTracking().SingleAsync(b => b.Id == patron.Id)).KioskPinHash);
    }

    [Fact]
    public async Task Staff_PinPage_ForAnUnknownBorrower_IsNotFound()
    {
        using var testDb = new SqliteTestDb();
        using var db = testDb.Open();
        var controller = Staff(db, new KioskBrowser(db));

        Assert.IsType<NotFoundResult>(await controller.KioskPin(99999));
        Assert.IsType<NotFoundResult>(await controller.KioskPin(99999, new KioskPinForm { Pin = Pin, ConfirmPin = Pin }));
    }

    [Fact]
    public async Task EditingABorrower_KeepsTheirPin()
    {
        using var testDb = new SqliteTestDb();
        using var db = testDb.Open();
        var patron = await Section1.AddBorrowerAsync(db, "Alice");
        var browser = new KioskBrowser(db);
        await browser.GivePinAsync(patron);
        var hash = (await db.Borrowers.AsNoTracking().SingleAsync(b => b.Id == patron.Id)).KioskPinHash;
        db.ChangeTracker.Clear();

        await Staff(db, browser).Edit(new Borrower { Id = patron.Id, FullName = "Alice Lee", Email = "a@example.com", Phone = "0400000000", Address = "2 St" });

        var stored = await db.Borrowers.AsNoTracking().SingleAsync(b => b.Id == patron.Id);
        Assert.Equal("Alice Lee", stored.FullName);
        Assert.Equal(hash, stored.KioskPinHash);
    }

    [Fact]
    public void PinFields_CanNeverBeBoundFromAForm()
    {
        foreach (var name in new[] { nameof(Borrower.KioskPinHash), nameof(Borrower.KioskPinSetAtUtc),
                                     nameof(Borrower.KioskPinFailedAttempts), nameof(Borrower.KioskPinLockedUntilUtc) })
            Assert.NotNull(typeof(Borrower).GetProperty(name)!.GetCustomAttribute<BindNeverAttribute>());
    }

    [Fact]
    public void ReceptionAndAdminCanSetPins_ButOnlyReceptionEditsBorrowers()
    {
        var type = typeof(BorrowersController);
        Assert.Equal("Reception,Admin", type.GetCustomAttribute<AuthorizeAttribute>()!.Roles);

        foreach (var method in type.GetMethods().Where(m => m.Name is "Create" or "Edit" or "Delete" or "DeleteConfirmed"))
            Assert.Equal("Reception", method.GetCustomAttribute<AuthorizeAttribute>()?.Roles);

        foreach (var method in type.GetMethods().Where(m => m.Name is "KioskPin" or "Index"))
            Assert.Null(method.GetCustomAttribute<AuthorizeAttribute>());

        var post = type.GetMethods().Single(m => m.Name == "KioskPin" && m.GetCustomAttribute<HttpPostAttribute>() != null);
        Assert.NotNull(post.GetCustomAttribute<ValidateAntiForgeryTokenAttribute>());
    }

    [Fact]
    public async Task DemoData_HasNoKioskPins()
    {
        using var host = await IdentityTestHost.CreateAsync();

        await DbSeeder.SeedAsync(host.Db, host.RoleManager, host.UserManager);

        Assert.True(await host.Db.Borrowers.AnyAsync());
        Assert.False(await host.Db.Borrowers.AnyAsync(b => b.KioskPinHash != null));
    }
}
