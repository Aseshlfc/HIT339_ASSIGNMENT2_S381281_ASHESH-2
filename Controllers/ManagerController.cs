using LibraryManagementSystem.Data;
using LibraryManagementSystem.Models;
using LibraryManagementSystem.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LibraryManagementSystem.Controllers;

[Authorize(Roles = "Manager")]
public class ManagerController : Controller
{
    private readonly LibraryContext _db;
    public ManagerController(LibraryContext db) => _db = db;

    public async Task<IActionResult> Index()
    {
        var now = DateTime.Now;
        var loans = await _db.Loans.AsNoTracking().ToListAsync();
        var items = await _db.Items.AsNoTracking().ToListAsync();

        var model = new ManagerDashboardViewModel
        {
            TotalLoans = loans.Count,
            ActiveLoans = loans.Count(l => !l.ReturnedAt.HasValue),
            ReturnedLoans = loans.Count(l => l.ReturnedAt.HasValue),
            OverdueLoans = loans.Count(l => !l.ReturnedAt.HasValue && l.DueDate.Date < now.Date),
            StatusCounts = Enum.GetValues<ItemStatus>()
                .ToDictionary(s => s.ToString(), s => items.Count(i => i.Status == s)),
            TypeCounts = new Dictionary<string, int>
            {
                ["Books"] = items.OfType<Book>().Count(),
                ["Music"] = items.OfType<Music>().Count(),
                ["Toys"] = items.OfType<Toy>().Count()
            },
            TotalFines = loans.Sum(l => l.FineAmount),
            PaidFines = loans.Where(l => l.FinePaid).Sum(l => l.FineAmount),
            OutstandingFines = loans.Where(l => !l.FinePaid).Sum(l => l.FineAmount)
        };

        model.PopularItems = await _db.Loans
            .AsNoTracking()
            .GroupBy(l => new { l.Item.Name, l.Item.LibraryCode })
            .Select(g => new PopularItemRow
            {
                Name = g.Key.Name,
                LibraryCode = g.Key.LibraryCode,
                BorrowCount = g.Count()
            })
            .OrderByDescending(x => x.BorrowCount)
            .ThenBy(x => x.Name)
            .Take(10)
            .ToListAsync();

        return View(model);
    }
}
