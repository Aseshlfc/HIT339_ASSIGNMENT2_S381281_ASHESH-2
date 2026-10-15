using LibraryManagementSystem.Models;
using LibraryManagementSystem.Services;
using LibraryManagementSystem.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using LibraryManagementSystem.Data;

namespace LibraryManagementSystem.Controllers;

/// <summary>
/// Public self-service kiosk. No staff login: a patron identifies themselves with
/// a library card number. Each physical kiosk is opened at its own branch address,
/// /kiosk/{branchCode}, and can only lend items held at that branch.
/// </summary>
[Route("kiosk")]
public class KioskController : Controller
{
    private readonly LibraryContext _db;
    private readonly IKioskService _kiosk;

    public KioskController(LibraryContext db, IKioskService kiosk)
    {
        _db = db;
        _kiosk = kiosk;
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

    [HttpGet("{branchCode}")]
    public async Task<IActionResult> Welcome(string branchCode)
    {
        var branch = await FindBranchAsync(branchCode);
        if (branch == null) return BranchNotFound();
        return View(new KioskWelcomeViewModel { Branch = branch });
    }

    [HttpPost("{branchCode}/account")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Account(string branchCode, KioskCardForm form)
    {
        var branch = await FindBranchAsync(branchCode);
        if (branch == null) return BranchNotFound();

        var patron = await _kiosk.FindPatronAsync(form.CardNumber);
        if (patron == null)
        {
            return View(nameof(Welcome), new KioskWelcomeViewModel
            {
                Branch = branch,
                CardNumber = form.CardNumber,
                Error = string.IsNullOrWhiteSpace(form.CardNumber)
                    ? "Scan or type your library card number."
                    : "That card number was not recognised. Check the number on your card and try again."
            });
        }

        return View(nameof(Account), new KioskAccountViewModel
        {
            Branch = branch,
            Account = await _kiosk.GetAccountAsync(patron)
        });
    }

    [HttpPost("{branchCode}/checkout")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Checkout(string branchCode, KioskCheckoutForm form)
    {
        var branch = await FindBranchAsync(branchCode);
        if (branch == null) return BranchNotFound();

        var patron = await _kiosk.FindPatronAsync(form.CardNumber);
        if (patron == null)
        {
            return View(nameof(Welcome), new KioskWelcomeViewModel
            {
                Branch = branch,
                Error = "Your session ended. Please scan your library card again."
            });
        }

        var result = await _kiosk.CheckOutAsync(branch, patron, form.LibraryCode);

        return View(nameof(Account), new KioskAccountViewModel
        {
            Branch = branch,
            Account = await _kiosk.GetAccountAsync(patron),
            Message = result.Message,
            MessageIsError = !result.Succeeded,
            LibraryCode = result.Succeeded ? null : form.LibraryCode
        });
    }

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
