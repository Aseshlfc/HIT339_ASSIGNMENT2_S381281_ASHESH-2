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
    // One run sends due-soon reminders and notices for overdue loans whose fines are building up.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RunDueDateCheck()
    {
        var dueSoon = await _reminders.SendDueSoonRemindersAsync();
        var overdue = await _reminders.SendOverdueFineNoticesAsync();
        TempData["Success"] = dueSoon + overdue == 0
            ? "Due-date check finished. No new reminders or overdue notices were needed."
            : $"Due-date check finished. {dueSoon} due-soon reminder(s) and {overdue} overdue fine notice(s) sent.";
        return RedirectToAction(nameof(Index));
    }
}