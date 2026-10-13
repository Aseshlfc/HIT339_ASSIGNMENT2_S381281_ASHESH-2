using LibraryManagementSystem.Data;
using LibraryManagementSystem.Models;
using LibraryManagementSystem.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LibraryManagementSystem.Controllers;

/// <summary>
/// Staff dashboard showing the history of simulated email/SMS notifications.
/// </summary>
[Authorize(Roles = "Reception")]
public class NotificationsController : Controller
{
    private readonly LibraryContext _db;
    private readonly IDueDateReminderService _reminders;

    public NotificationsController(LibraryContext db, IDueDateReminderService reminders)
    {
        _db = db;
        _reminders = reminders;
    }

    // Lists the newest notifications first, optionally filtered by type.
    public async Task<IActionResult> Index(NotificationType? type = null)
    {
        var query = _db.Notifications
            .Include(n => n.Borrower)
            .AsNoTracking()
            .AsQueryable();

        if (type.HasValue) query = query.Where(n => n.Type == type);

        ViewBag.SelectedType = type;
        return View(await query.OrderByDescending(n => n.CreatedAt).Take(200).ToListAsync());
    }

    // Staff trigger for the due-date check (a stand-in for a scheduled background job).
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RunDueDateCheck()
    {
        var sent = await _reminders.SendDueSoonRemindersAsync();
        TempData["Success"] = sent == 0
            ? "Due-date check finished. No new reminders were needed."
            : $"Due-date check finished. {sent} reminder(s) sent.";
        return RedirectToAction(nameof(Index));
    }
}