using LibraryManagementSystem.Data;
using LibraryManagementSystem.Models;
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
    public NotificationsController(LibraryContext db) => _db = db;

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
}