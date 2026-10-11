using LibraryManagementSystem.Data;
using LibraryManagementSystem.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace LibraryManagementSystem.Controllers;

public class HomeController : Controller
{
    private readonly LibraryContext _db;

    public HomeController(LibraryContext db) => _db = db;

    // Target of UseExceptionHandler("/Home/Error") in Program.cs.
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error() => View();

    public async Task<IActionResult> Index(string? q, string? type, bool availableOnly = false, int? branchId = null)
    {
        var query = _db.Items.AsNoTracking().Where(i => i.Status != ItemStatus.Destroy);

        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim().ToLower();
            query = query.Where(i =>
                i.Name.ToLower().Contains(term) ||
                i.Description.ToLower().Contains(term) ||
                i.LibraryCode.ToLower().Contains(term));
        }

        if (!string.IsNullOrWhiteSpace(type))
        {
            query = type switch
            {
                "Book" => query.OfType<Book>(),
                "Music" => query.OfType<Music>(),
                "Toy" => query.OfType<Toy>(),
                _ => query
            };
        }

        if (availableOnly)
            query = query.Where(i => i.Status == ItemStatus.Available);

        if (branchId.HasValue)
            query = query.Where(i => i.BranchId == branchId);

        var branches = await _db.Branches.AsNoTracking().OrderBy(b => b.Name).ToListAsync();
        ViewBag.Branches = branches;
        ViewBag.BranchNames = branches.ToDictionary(b => b.Id, b => b.Name);
        ViewBag.BranchId = branchId;

        ViewBag.Query = q;
        ViewBag.Type = type;
        ViewBag.AvailableOnly = availableOnly;
        ViewBag.Types = new SelectList(new[]
        {
            new { Value = "Book", Text = "Books" },
            new { Value = "Music", Text = "Music" },
            new { Value = "Toy", Text = "Toys" }
        }, "Value", "Text", type);

        return View(await query.OrderBy(i => i.Name).ToListAsync());
    }
}
