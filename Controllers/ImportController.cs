using System.Text;
using LibraryManagementSystem.Services;
using LibraryManagementSystem.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LibraryManagementSystem.Controllers;

/// <summary>Admin-only bulk import of catalogue items from a CSV file.</summary>
[Authorize(Roles = "Admin")]
public class ImportController : Controller
{
    private readonly IItemImportService _import;

    public const long RequestLimitBytes = 20 * ItemImportService.MaxBytes;

    public ImportController(IItemImportService import) => _import = import;

    [HttpGet]
    public IActionResult Index() => View(new ImportPageViewModel { ValidateOnly = true });

    [HttpPost]
    [ValidateAntiForgeryToken]
    // The request limit is well above the 1 MB file limit so that a file that is too big
    // is answered with the importer's own message rather than a dropped connection.
    [RequestSizeLimit(RequestLimitBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = RequestLimitBytes)]
    public async Task<IActionResult> Index(IFormFile? csvFile, bool validateOnly = false, bool stopOnAnyError = false,
        CancellationToken cancellationToken = default)
    {
        var model = new ImportPageViewModel { ValidateOnly = validateOnly, StopOnAnyError = stopOnAnyError };

        if (csvFile == null || csvFile.Length == 0)
        {
            ModelState.AddModelError("csvFile", "Choose a CSV file to upload.");
            return View(model);
        }

        if (!string.Equals(Path.GetExtension(csvFile.FileName), ".csv", StringComparison.OrdinalIgnoreCase))
        {
            ModelState.AddModelError("csvFile", "Only .csv files can be imported.");
            return View(model);
        }

        await using var stream = csvFile.OpenReadStream();
        model.FileName = Path.GetFileName(csvFile.FileName);
        model.Report = await _import.ImportAsync(stream, new ImportOptions(validateOnly, stopOnAnyError), cancellationToken);

        return View(model);
    }

    [HttpGet]
    public IActionResult Template()
    {
        // UTF-8 with a byte order mark so Excel opens it correctly.
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(_import.TemplateCsv())).ToArray();
        return File(bytes, "text/csv", "items-import-template.csv");
    }
}
