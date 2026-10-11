using LibraryManagementSystem.Data;
using LibraryManagementSystem.Services;
using LibraryManagementSystem.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LibraryManagementSystem.Controllers;

[Authorize(Roles = "Manager")]
public class ManagerController : Controller
{
    public const string Borrowing = "borrowing";
    public const string Fines = "fines";
    public const string Inventory = "inventory";

    private readonly LibraryContext _db;
    private readonly IReportService _reports;
    private readonly IReportPdfService _pdf;

    public ManagerController(LibraryContext db, IReportService reports, IReportPdfService pdf)
    {
        _db = db;
        _reports = reports;
        _pdf = pdf;
    }

    public async Task<IActionResult> Index()
        => View(await _reports.BuildDashboardAsync());

    public Task<IActionResult> BorrowingReport(ReportFilter filter) => ReportPageAsync(Borrowing, filter);

    public Task<IActionResult> FineAudit(ReportFilter filter) => ReportPageAsync(Fines, filter);

    public Task<IActionResult> InventoryHealth(ReportFilter filter) => ReportPageAsync(Inventory, filter);

    /// <summary>
    /// Downloads a report as CSV or PDF using the same filters as the page.
    /// </summary>
    public async Task<IActionResult> Export(string report, string format, ReportFilter filter)
    {
        var document = await BuildAsync(report, filter);
        if (document == null) return NotFound();

        return (format ?? string.Empty).ToLowerInvariant() switch
        {
            "csv" => File(ReportCsvWriter.Write(document), "text/csv", document.FileName("csv")),
            "pdf" => File(_pdf.Render(document), "application/pdf", document.FileName("pdf")),
            _ => BadRequest("Unsupported export format. Use csv or pdf.")
        };
    }

    private async Task<IActionResult> ReportPageAsync(string key, ReportFilter filter)
    {
        var document = await BuildAsync(key, filter);
        return View("Report", new ReportPageViewModel
        {
            ReportKey = key,
            Document = document!,
            Filter = filter,
            Branches = await _db.Branches.AsNoTracking().OrderBy(b => b.Name).ToListAsync(),
            ShowDateRange = key != Inventory,
            ShowFineStatus = key == Fines,
            DateLabel = key == Fines ? "Returned" : "Borrowed"
        });
    }

    private async Task<ReportDocument?> BuildAsync(string? key, ReportFilter filter)
    {
        // Only known values are accepted for the drop-down filters; anything
        // else (for example a hand-edited URL) is treated as "all".
        filter.ItemType = new[] { "Book", "Music", "Toy" }
            .FirstOrDefault(t => string.Equals(t, filter.ItemType, StringComparison.OrdinalIgnoreCase));
        filter.FineStatus = new[] { "Paid", "Outstanding" }
            .FirstOrDefault(s => string.Equals(s, filter.FineStatus, StringComparison.OrdinalIgnoreCase));
        if (filter.BranchId.HasValue && !await _db.Branches.AnyAsync(b => b.Id == filter.BranchId))
            filter.BranchId = null;

        // A reversed date range is treated as the range the user meant.
        if (filter.From.HasValue && filter.To.HasValue && filter.From > filter.To)
            (filter.From, filter.To) = (filter.To, filter.From);

        return (key ?? string.Empty).ToLowerInvariant() switch
        {
            Borrowing => await _reports.BuildBorrowingReportAsync(filter),
            Fines => await _reports.BuildFineAuditReportAsync(filter),
            Inventory => await _reports.BuildInventoryHealthReportAsync(filter),
            _ => null
        };
    }
}
