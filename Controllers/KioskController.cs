using LibraryManagementSystem.Models;
using LibraryManagementSystem.Services;
using LibraryManagementSystem.ViewModels;
using LibraryManagementSystem.Api;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using LibraryManagementSystem.Data;

namespace LibraryManagementSystem.Controllers;

/// <summary>
/// Public self-service kiosk. No staff login: a patron identifies themselves with
/// a library card number and a 6-digit PIN set by staff. Each physical kiosk is opened at its own branch address,
/// /kiosk/{branchCode}, and can only lend items held at that branch. After sign-in
/// the patron is held in a short server-side session (see KioskSessionStore).
/// </summary>
[Route("kiosk")]
public class KioskController : Controller
{
    private readonly LibraryContext _db;
    private readonly IKioskService _kiosk;
    private readonly IKioskSessionStore _sessions;
    private readonly IKioskPinService _pins;

    public KioskController(LibraryContext db, IKioskService kiosk, IKioskSessionStore sessions, IKioskPinService pins)
    {
        _db = db;
        _kiosk = kiosk;
        _sessions = sessions;
        _pins = pins;
    }

    // Branch picker. A library with one active branch skips straight to it.
    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        var branches = await _db.Branches.AsNoTracking()
            .Where(b => b.IsActive)
            .OrderBy(b => b.Name)
            .ToListAsync();

        if (branches.Count == 1)
            return RedirectToAction(nameof(Welcome), new { branchCode = branches[0].Code });

        return View(branches);
    }

    // The welcome screen is where nobody is signed in, so arriving here (by Finish,
    // the idle timer, Back or a bookmark) always ends any kiosk session first.
    [HttpGet("{branchCode}")]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public async Task<IActionResult> Welcome(string branchCode, bool signedOut = false, bool ended = false)
    {
        var branch = await FindBranchAsync(branchCode);
        if (branch == null) return BranchNotFound();

        EndSession();
        return WelcomeView(branch, error: ended ? SessionEndedMessage : null, signedOut: signedOut && !ended);
    }

    // Post/Redirect/Get: a recognised card and correct PIN start a new server-side session and the
    // browser is sent to GET /account, so the card number post never stays in history.
    [HttpPost("{branchCode}/account")]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting(KioskOptions.PolicyName)]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public async Task<IActionResult> OpenAccount(string branchCode, KioskCardForm form)
    {
        var branch = await FindBranchAsync(branchCode);
        if (branch == null) return BranchNotFound();

        // Never carry an earlier session into a new sign-in (session fixation).
        EndSession();

        // Each welcome screen can be used once. A resubmitted or replayed sign-in
        // form is refused even though the card number in it is valid.
        if (!_sessions.RedeemSignInToken(form.SignInToken, branch.Id))
            return WelcomeView(branch, error: "This screen has expired. Please scan your library card again.");

        var result = await _pins.SignInAsync(form.CardNumber, form.Pin);
        if (result.Status != KioskSignInStatus.Success)
        {
            // The card number is only kept when the PIN is all that is missing, and the PIN never is.
            return WelcomeView(branch,
                cardNumber: result.Status == KioskSignInStatus.MissingPin ? form.CardNumber : null,
                error: result.Status switch
                {
                    KioskSignInStatus.MissingCard => "Scan or type your library card number.",
                    KioskSignInStatus.MissingPin => "Type your 6-digit kiosk PIN.",
                    KioskSignInStatus.NoPinSet => "This card does not have a kiosk PIN yet. Please ask library staff at the desk to set one up.",
                    KioskSignInStatus.LockedOut => "Too many incorrect PINs. This card cannot be used at the kiosk for 15 minutes. Library staff at the desk can help.",
                    _ => "That card number and PIN do not match. Check them and try again."
                });
        }

        var patron = result.Patron!;
        var session = _sessions.Start(patron.Id, branch.Id, patron.KioskPinSetAtUtc);
        Response.Cookies.Append(SessionCookieName, session.Id, SessionCookieOptions());
        return RedirectToAction(nameof(Account), new { branchCode = branch.Code });
    }

    [HttpGet("{branchCode}/account")]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public async Task<IActionResult> Account(string branchCode)
    {
        var branch = await FindBranchAsync(branchCode);
        if (branch == null) return BranchNotFound();

        var (session, patron) = await CurrentPatronAsync(branch);
        if (session == null || patron == null) return SessionEnded(branch);

        var flash = session.TakeFlash();
        return View(nameof(Account), new KioskAccountViewModel
        {
            Branch = branch,
            Account = await _kiosk.GetAccountAsync(patron),
            Message = flash?.Message,
            MessageIsError = flash?.IsError ?? false,
            LibraryCode = flash?.LibraryCode
        });
    }

    // The borrower is the session's patron at the session's branch. Nothing about who
    // is borrowing is read from the form.
    [HttpPost("{branchCode}/checkout")]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting(KioskOptions.PolicyName)]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public async Task<IActionResult> Checkout(string branchCode, KioskCheckoutForm form)
    {
        var branch = await FindBranchAsync(branchCode);
        if (branch == null) return BranchNotFound();

        var (session, patron) = await CurrentPatronAsync(branch);
        if (session == null || patron == null) return SessionEnded(branch);

        var result = await _kiosk.CheckOutAsync(branch, patron, form.LibraryCode);
        session.Flash = new KioskFlash(result.Message, !result.Succeeded, result.Succeeded ? null : form.LibraryCode);
        return RedirectToAction(nameof(Account), new { branchCode = branch.Code });
    }

    // "Finish and sign out" and the idle timer. The session is deleted on the server,
    // so a copied cookie or an old page can no longer reach the account.
    [HttpPost("{branchCode}/finish")]
    [ValidateAntiForgeryToken]
    public IActionResult Finish(string branchCode)
    {
        EndSession();
        return RedirectToAction(nameof(Welcome), new { branchCode, signedOut = true });
    }

    private const string SessionCookieName = "LibraryKiosk";
    private const string SessionEndedMessage = "Your session ended. Please scan your library card again.";

    private ViewResult WelcomeView(Branch branch, string? error = null, string? cardNumber = null, bool signedOut = false) =>
        View(nameof(Welcome), new KioskWelcomeViewModel
        {
            Branch = branch,
            CardNumber = cardNumber,
            Error = error,
            SignedOut = signedOut,
            SignInToken = _sessions.CreateSignInToken(branch.Id)
        });

    private async Task<(KioskSession? Session, Borrower? Patron)> CurrentPatronAsync(Branch branch)
    {
        var session = _sessions.Find(Request.Cookies[SessionCookieName]);
        if (session == null) return (null, null);

        // A session only works at the kiosk where it started.
        if (session.BranchId != branch.Id) return (null, null);

        var patron = await _db.Borrowers.AsNoTracking().SingleOrDefaultAsync(b => b.Id == session.PatronId);

        // Staff changed the PIN (or the patron was removed) after this session began.
        if (patron == null || patron.KioskPinHash == null || patron.KioskPinSetAtUtc != session.PinSetAtUtc)
            return (null, null);

        return (session, patron);
    }

    private RedirectToActionResult SessionEnded(Branch branch)
    {
        EndSession();
        return RedirectToAction(nameof(Welcome), new { branchCode = branch.Code, ended = true });
    }

    private void EndSession()
    {
        if (Request.Cookies.TryGetValue(SessionCookieName, out var sessionId))
            _sessions.End(sessionId);
        Response.Cookies.Delete(SessionCookieName, SessionCookieOptions());
    }

    // HttpOnly so page scripts cannot read it, SameSite=Strict so other sites cannot
    // use it, limited to /kiosk, and with no expiry date so it is never written to disk.
    private CookieOptions SessionCookieOptions() => new()
    {
        HttpOnly = true,
        SameSite = SameSiteMode.Strict,
        Secure = Request.IsHttps,
        Path = "/kiosk",
        IsEssential = true
    };

    private async Task<Branch?> FindBranchAsync(string branchCode)
    {
        var code = branchCode.Trim().ToUpperInvariant();
        return await _db.Branches.AsNoTracking()
            .FirstOrDefaultAsync(b => b.IsActive && b.Code.ToUpper() == code);
    }

    private ViewResult BranchNotFound()
    {
        Response.StatusCode = StatusCodes.Status404NotFound;
        return View("BranchNotFound");
    }
}
