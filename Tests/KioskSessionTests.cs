using LibraryManagementSystem.Api;
using LibraryManagementSystem.Controllers;
using LibraryManagementSystem.Data;
using LibraryManagementSystem.Models;
using LibraryManagementSystem.Services;
using LibraryManagementSystem.ViewModels;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace LibraryManagementSystem.Tests;

internal sealed class FakeClock : TimeProvider
{
    public DateTimeOffset Now { get; set; } = new(2026, 10, 15, 9, 0, 0, TimeSpan.Zero);
    public override DateTimeOffset GetUtcNow() => Now;
    public void Advance(TimeSpan by) => Now += by;
}

/// <summary>
/// Plays the part of the kiosk's browser: one cookie jar and one server-side session
/// store shared across requests, with a fresh controller and HttpContext per request.
/// </summary>
internal sealed class KioskBrowser
{
    public const string CookieName = "LibraryKiosk";

    /// <summary>A PIN used only by these tests. The leading zero is deliberate.</summary>
    public const string TestPin = "024680";

    private readonly LibraryContext _db;

    public KioskBrowser(LibraryContext db, KioskSessionStore? store = null, FakeClock? clock = null)
    {
        _db = db;
        Clock = clock ?? new FakeClock();
        Store = store ?? new KioskSessionStore(new EphemeralDataProtectionProvider(), Options.Create(new KioskOptions()), Clock);
        Pins = new KioskPinService(db, Store, Clock);
    }

    public KioskPinService Pins { get; }

    /// <summary>Gives the patron the test PIN, as staff would on the Borrowers page.</summary>
    public async Task GivePinAsync(Borrower patron, string pin = TestPin) => Assert.True(await Pins.SetPinAsync(patron.Id, pin));

    public FakeClock Clock { get; }
    public KioskSessionStore Store { get; }
    public string? SessionCookie { get; set; }

    /// <summary>The Set-Cookie headers from the most recent request.</summary>
    public IReadOnlyList<string> LastSetCookies { get; private set; } = Array.Empty<string>();

    public KioskController Controller()
    {
        var kiosk = new KioskService(_db, new NotificationService(_db, NullLogger<NotificationService>.Instance));
        var controller = new KioskController(_db, kiosk, Store, Pins);
        ControllerTestHelpers.AttachContext(controller);
        if (SessionCookie != null)
            controller.HttpContext.Request.Headers.Cookie = $"{CookieName}={SessionCookie}";
        return controller;
    }

    public async Task<T> Send<T>(Func<KioskController, Task<IActionResult>> request) where T : IActionResult
    {
        var controller = Controller();
        var result = await request(controller);
        LastSetCookies = controller.Response.Headers.SetCookie.Select(c => c!).ToList();
        foreach (var header in LastSetCookies.Where(h => h.StartsWith(CookieName + "=")))
        {
            var value = header[(CookieName.Length + 1)..].Split(';')[0];
            SessionCookie = value.Length == 0 ? null : value;
        }
        return Assert.IsType<T>(result);
    }

    public Task<T> Send<T>(Func<KioskController, IActionResult> request) where T : IActionResult =>
        Send<T>(c => Task.FromResult(request(c)));

    public async Task<string> WelcomeTokenAsync(string branchCode = "CEN")
    {
        var view = await Send<ViewResult>(c => c.Welcome(branchCode));
        return Assert.IsType<KioskWelcomeViewModel>(view.Model).SignInToken;
    }

    /// <summary>Signs in with the test PIN, giving the patron that PIN first if they have none.</summary>
    public async Task<KioskAccountViewModel> SignInAsync(Borrower patron, string branchCode = "CEN")
    {
        if (!await _db.Borrowers.AnyAsync(b => b.Id == patron.Id && b.KioskPinHash != null))
            await GivePinAsync(patron);
        var token = await WelcomeTokenAsync(branchCode);
        await Send<RedirectToActionResult>(c => c.OpenAccount(branchCode, new KioskCardForm { CardNumber = patron.CardNumber, Pin = TestPin, SignInToken = token }));
        return await AccountAsync(branchCode);
    }

    public async Task<KioskAccountViewModel> AccountAsync(string branchCode = "CEN")
    {
        var view = await Send<ViewResult>(c => c.Account(branchCode));
        return Assert.IsType<KioskAccountViewModel>(view.Model);
    }

    /// <summary>True if the account page now sends the browser to the welcome screen.</summary>
    public async Task<bool> AccountIsClosedAsync(string branchCode = "CEN")
    {
        var controller = Controller();
        var result = await controller.Account(branchCode);
        return result is RedirectToActionResult { ActionName: "Welcome" };
    }
}

/// <summary>
/// Security of the kiosk's server-side session: Back, refresh, resubmission and
/// replay after sign-out, expiry, session fixation and borrowing on someone else's card.
/// </summary>
public class KioskSessionTests
{
    [Fact]
    public async Task SignIn_IssuesAFreshRandomHttpOnlyStrictCookieLimitedToTheKiosk()
    {
        using var testDb = new SqliteTestDb();
        using var db = testDb.Open();
        var patron = await Section1.AddBorrowerAsync(db);
        var browser = new KioskBrowser(db);

        await browser.SignInAsync(patron);
        var first = browser.SessionCookie;
        await browser.Send<RedirectToActionResult>(c => c.Finish("CEN"));
        await browser.SignInAsync(patron);

        Assert.Matches("^[0-9a-f]{64}$", first);                 // 256 random bits
        Assert.NotEqual(first, browser.SessionCookie);            // never reused
        Assert.DoesNotContain(patron.CardNumber, first!, StringComparison.OrdinalIgnoreCase);

        var controller = browser.Controller();
        controller.HttpContext.Request.Headers.Cookie = "";
        var token = browser.Store.CreateSignInToken(Branch.DefaultBranchId);
        await controller.OpenAccount("CEN", new KioskCardForm { CardNumber = patron.CardNumber, Pin = KioskBrowser.TestPin, SignInToken = token });
        var setCookie = controller.Response.Headers.SetCookie.Last()!.ToLowerInvariant();
        Assert.Contains("httponly", setCookie);
        Assert.Contains("samesite=strict", setCookie);
        Assert.Contains("path=/kiosk", setCookie);
        Assert.DoesNotContain("expires=", setCookie);            // never written to disk
    }

    [Fact]
    public async Task Finish_EndsTheSessionOnTheServer_SoBackRefreshAndACopiedCookieAllFail()
    {
        using var testDb = new SqliteTestDb();
        using var db = testDb.Open();
        var patron = await Section1.AddBorrowerAsync(db);
        var browser = new KioskBrowser(db);
        await browser.SignInAsync(patron);
        var copiedCookie = browser.SessionCookie;

        await browser.Send<RedirectToActionResult>(c => c.Finish("CEN"));

        // The browser's cookie is cleared...
        Assert.Null(browser.SessionCookie);
        // ...and Back or Refresh on the account page goes to the welcome screen.
        Assert.True(await browser.AccountIsClosedAsync());

        // Even someone who kept the old cookie value gets nothing: the session is gone.
        browser.SessionCookie = copiedCookie;
        Assert.True(await browser.AccountIsClosedAsync());
        Assert.Null(browser.Store.Find(copiedCookie));
    }

    [Fact]
    public async Task ReplayingTheOldSignInPost_IsRefused_BeforeAndAfterSignOut()
    {
        using var testDb = new SqliteTestDb();
        using var db = testDb.Open();
        var patron = await Section1.AddBorrowerAsync(db);
        var browser = new KioskBrowser(db);
        await browser.GivePinAsync(patron);
        var token = await browser.WelcomeTokenAsync();
        var oldPost = new KioskCardForm { CardNumber = patron.CardNumber, Pin = KioskBrowser.TestPin, SignInToken = token };

        await browser.Send<RedirectToActionResult>(c => c.OpenAccount("CEN", oldPost));
        await browser.Send<RedirectToActionResult>(c => c.Finish("CEN"));

        // "Confirm Form Resubmission" sends exactly the same form again: card number,
        // anti-forgery token and sign-in token. It must not start a new session.
        var replay = await browser.Send<ViewResult>(c => c.OpenAccount("CEN", oldPost));
        Assert.Equal("Welcome", replay.ViewName);
        Assert.Contains("expired", Assert.IsType<KioskWelcomeViewModel>(replay.Model).Error);
        Assert.Null(browser.SessionCookie);
        Assert.True(await browser.AccountIsClosedAsync());

        // Replaying it from a different browser (no cookies at all) fails the same way.
        var stranger = new KioskBrowser(db, browser.Store, browser.Clock);
        Assert.IsType<ViewResult>(await stranger.Controller().OpenAccount("CEN", oldPost));
        Assert.True(await stranger.AccountIsClosedAsync());
    }

    [Fact]
    public async Task ResubmittingAFailedSignIn_IsRefused_AndDoesNotRevealAnything()
    {
        using var testDb = new SqliteTestDb();
        using var db = testDb.Open();
        var browser = new KioskBrowser(db);
        var token = await browser.WelcomeTokenAsync();
        var post = new KioskCardForm { CardNumber = "P9999", Pin = KioskBrowser.TestPin, SignInToken = token };

        await browser.Send<ViewResult>(c => c.OpenAccount("CEN", post));
        var again = await browser.Send<ViewResult>(c => c.OpenAccount("CEN", post));

        var model = Assert.IsType<KioskWelcomeViewModel>(again.Model);
        Assert.Contains("expired", model.Error);
        Assert.Null(model.CardNumber);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("altered")]
    [InlineData("other-branch")]
    public async Task SignIn_WithAMissingAlteredOrOtherBranchToken_IsRefused(string kind)
    {
        using var testDb = new SqliteTestDb();
        using var db = testDb.Open();
        var (_, north, _) = await Section1.SeedBranchesAsync(db);
        var patron = await Section1.AddBorrowerAsync(db);
        var browser = new KioskBrowser(db);
        await browser.GivePinAsync(patron);
        var good = await browser.WelcomeTokenAsync();
        var token = kind switch
        {
            "missing" => null,
            "altered" => good[..^4] + (good.EndsWith("AAAA") ? "BBBB" : "AAAA"),
            _ => browser.Store.CreateSignInToken(north.Id)
        };

        var view = await browser.Send<ViewResult>(c => c.OpenAccount("CEN", new KioskCardForm { CardNumber = patron.CardNumber, Pin = KioskBrowser.TestPin, SignInToken = token }));

        Assert.Equal("Welcome", view.ViewName);
        Assert.Null(browser.SessionCookie);
    }

    [Fact]
    public async Task SignInForm_StopsWorkingAfterItsLifetime()
    {
        using var testDb = new SqliteTestDb();
        using var db = testDb.Open();
        var patron = await Section1.AddBorrowerAsync(db);
        var browser = new KioskBrowser(db);
        await browser.GivePinAsync(patron);
        var token = await browser.WelcomeTokenAsync();

        browser.Clock.Advance(TimeSpan.FromMinutes(new KioskOptions().SignInFormMinutes));

        await browser.Send<ViewResult>(c => c.OpenAccount("CEN", new KioskCardForm { CardNumber = patron.CardNumber, Pin = KioskBrowser.TestPin, SignInToken = token }));
        Assert.Null(browser.SessionCookie);
    }

    [Fact]
    public async Task Session_EndsOnTheServerAfterIdleTime_EvenIfNobodyPressesFinish()
    {
        using var testDb = new SqliteTestDb();
        using var db = testDb.Open();
        var patron = await Section1.AddBorrowerAsync(db);
        var browser = new KioskBrowser(db);
        var options = new KioskOptions();

        await browser.SignInAsync(patron);
        browser.Clock.Advance(TimeSpan.FromMinutes(options.SessionIdleMinutes) - TimeSpan.FromSeconds(1));
        await browser.AccountAsync();                              // still open, and the idle clock restarts

        browser.Clock.Advance(TimeSpan.FromMinutes(options.SessionIdleMinutes));
        Assert.True(await browser.AccountIsClosedAsync());
    }

    [Fact]
    public async Task Session_EndsAfterTheMaximumTime_HoweverBusy()
    {
        using var testDb = new SqliteTestDb();
        using var db = testDb.Open();
        var patron = await Section1.AddBorrowerAsync(db);
        var browser = new KioskBrowser(db);
        var options = new KioskOptions();

        await browser.SignInAsync(patron);
        for (var minute = 1; minute < options.SessionMaxMinutes; minute++)
        {
            browser.Clock.Advance(TimeSpan.FromMinutes(1));
            await browser.AccountAsync();
        }
        browser.Clock.Advance(TimeSpan.FromMinutes(1));

        Assert.True(await browser.AccountIsClosedAsync());
    }

    [Fact]
    public async Task SignIn_NeverAdoptsASessionIdChosenByTheBrowser_AndEndsAnyEarlierSession()
    {
        using var testDb = new SqliteTestDb();
        using var db = testDb.Open();
        var alice = await Section1.AddBorrowerAsync(db, "Alice");
        var bob = await Section1.AddBorrowerAsync(db, "Bob");
        var browser = new KioskBrowser(db);
        await browser.GivePinAsync(alice);
        await browser.GivePinAsync(bob);

        // An attacker plants a cookie value before the patron signs in.
        const string planted = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        var token = await browser.WelcomeTokenAsync();
        browser.SessionCookie = planted;
        await browser.Send<RedirectToActionResult>(c => c.OpenAccount("CEN", new KioskCardForm { CardNumber = alice.CardNumber, Pin = KioskBrowser.TestPin, SignInToken = token }));

        Assert.NotEqual(planted, browser.SessionCookie);
        Assert.Null(browser.Store.Find(planted));
        var aliceSession = browser.SessionCookie;

        // Signing in again (Bob) on the same browser ends Alice's session on the server.
        var bobToken = browser.Store.CreateSignInToken(Branch.DefaultBranchId);
        await browser.Send<RedirectToActionResult>(c => c.OpenAccount("CEN", new KioskCardForm { CardNumber = bob.CardNumber, Pin = KioskBrowser.TestPin, SignInToken = bobToken }));
        Assert.Null(browser.Store.Find(aliceSession));
        Assert.Equal("Bob", (await browser.AccountAsync()).Account.Patron.FullName);
    }

    [Fact]
    public async Task VisitingTheWelcomeScreen_EndsTheSession()
    {
        using var testDb = new SqliteTestDb();
        using var db = testDb.Open();
        var patron = await Section1.AddBorrowerAsync(db);
        var browser = new KioskBrowser(db);
        await browser.SignInAsync(patron);
        var session = browser.SessionCookie;

        await browser.Send<ViewResult>(c => c.Welcome("CEN"));     // e.g. Back to the welcome screen

        Assert.Null(browser.Store.Find(session));
        browser.SessionCookie = session;
        Assert.True(await browser.AccountIsClosedAsync());
    }

    [Fact]
    public async Task Checkout_BorrowsOnlyForTheSessionPatron_AndStillSendsTheBorrowedNotification()
    {
        using var testDb = new SqliteTestDb();
        using var db = testDb.Open();
        var (central, _, _) = await Section1.SeedBranchesAsync(db);
        await Section1.AddBookAsync(db, "B100", central);
        var alice = await Section1.AddBorrowerAsync(db, "Alice");
        var bob = await Section1.AddBorrowerAsync(db, "Bob");
        var browser = new KioskBrowser(db);
        await browser.SignInAsync(alice);

        // The checkout form has no card number field to tamper with.
        Assert.Null(typeof(KioskCheckoutForm).GetProperty("CardNumber"));

        await browser.Send<RedirectToActionResult>(c => c.Checkout("CEN", new KioskCheckoutForm { LibraryCode = "B100" }));

        var loan = await db.Loans.AsNoTracking().SingleAsync();
        Assert.Equal(alice.Id, loan.BorrowerId);
        Assert.False(await db.Loans.AnyAsync(l => l.BorrowerId == bob.Id));
        Assert.True(await db.Notifications.AnyAsync(n => n.BorrowerId == alice.Id && n.Type == NotificationType.Borrowed));
    }

    [Fact]
    public async Task Session_FromOneBranch_CannotBeUsedAtAnother()
    {
        using var testDb = new SqliteTestDb();
        using var db = testDb.Open();
        var (_, north, _) = await Section1.SeedBranchesAsync(db);
        await Section1.AddBookAsync(db, "N100", north);
        var patron = await Section1.AddBorrowerAsync(db);
        var browser = new KioskBrowser(db);
        await browser.SignInAsync(patron, "CEN");

        var redirect = await browser.Send<RedirectToActionResult>(c => c.Checkout("NTH", new KioskCheckoutForm { LibraryCode = "N100" }));

        Assert.Equal("Welcome", redirect.ActionName);
        Assert.Equal(0, await db.Loans.CountAsync());
    }

    [Fact]
    public void SignInTokens_FromBeforeARestart_AreRefused()
    {
        var keys = new EphemeralDataProtectionProvider();
        var clock = new FakeClock();
        var beforeRestart = new KioskSessionStore(keys, Options.Create(new KioskOptions()), clock);
        var token = beforeRestart.CreateSignInToken(1);
        Assert.True(beforeRestart.RedeemSignInToken(token, 1));

        // Same keys, new process: the used-token list is empty, but the token still fails.
        var afterRestart = new KioskSessionStore(keys, Options.Create(new KioskOptions()), clock);
        Assert.False(afterRestart.RedeemSignInToken(token, 1));
        Assert.False(afterRestart.RedeemSignInToken(beforeRestart.CreateSignInToken(1), 1));
    }

    [Fact]
    public void SessionIds_AreRandomAndUnique()
    {
        var store = new KioskSessionStore(new EphemeralDataProtectionProvider(), Options.Create(new KioskOptions()), new FakeClock());

        var ids = Enumerable.Range(0, 500).Select(_ => store.Start(1, 1, null).Id).ToList();

        Assert.All(ids, id => Assert.Matches("^[0-9a-f]{64}$", id));
        Assert.Equal(ids.Count, ids.Distinct().Count());
    }
}
