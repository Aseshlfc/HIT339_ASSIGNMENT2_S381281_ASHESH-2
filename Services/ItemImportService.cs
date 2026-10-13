using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text;
using LibraryManagementSystem.Data;
using LibraryManagementSystem.Models;
using Microsoft.EntityFrameworkCore;

namespace LibraryManagementSystem.Services;

public enum ImportRowStatus
{
    /// <summary>Valid and saved to the catalogue.</summary>
    Imported,
    /// <summary>Valid but not saved (validate-only run, or the import was stopped).</summary>
    ReadyToImport,
    /// <summary>Has at least one problem; never saved.</summary>
    Invalid
}

public record ImportRowResult(
    int Line,
    string Type,
    string LibraryCode,
    string Name,
    ImportRowStatus Status,
    IReadOnlyList<string> Messages);

/// <param name="ValidateOnly">Check the file and report, but save nothing.</param>
/// <param name="StopOnAnyError">Save nothing unless every row is valid.</param>
public record ImportOptions(bool ValidateOnly = false, bool StopOnAnyError = false);

public class ImportReport
{
    public bool ValidateOnly { get; init; }
    public bool StopOnAnyError { get; init; }

    /// <summary>Set when the whole file was rejected (wrong format, missing columns, too big...).</summary>
    public string? FileError { get; set; }

    /// <summary>Set when valid rows could not be saved.</summary>
    public string? NotImportedReason { get; set; }

    public List<string> Warnings { get; } = new();
    public List<ImportRowResult> Rows { get; } = new();

    public bool FileAccepted => FileError == null;
    public int TotalRows => Rows.Count;
    public int ImportedCount => Rows.Count(r => r.Status == ImportRowStatus.Imported);
    public int ReadyCount => Rows.Count(r => r.Status == ImportRowStatus.ReadyToImport);
    public int InvalidCount => Rows.Count(r => r.Status == ImportRowStatus.Invalid);
}

public interface IItemImportService
{
    Task<ImportReport> ImportAsync(Stream csv, ImportOptions options, CancellationToken cancellationToken = default);

    /// <summary>A starter file with the right headers and one example row per item type.</summary>
    string TemplateCsv();
}

/// <summary>
/// Bulk-adds Books, Music and Toys from a CSV file. Every row is checked with the same
/// data-annotation rules the Admin item form uses, plus library-code and branch checks,
/// and the result says exactly which line has which problem.
/// </summary>
public class ItemImportService : IItemImportService
{
    public const long MaxBytes = 1_048_576;   // 1 MB
    public const int MaxRows = 1000;

    private static readonly string[] RequiredColumns = ["type", "name", "description", "librarycode"];

    private static readonly Dictionary<string, string> ColumnTitles = new()
    {
        ["type"] = "Type", ["name"] = "Name", ["description"] = "Description", ["librarycode"] = "LibraryCode",
        ["branchcode"] = "BranchCode", ["status"] = "Status",
        ["author"] = "Author", ["genre"] = "Genre", ["publicationyear"] = "PublicationYear",
        ["artist"] = "Artist", ["releaseyear"] = "ReleaseYear",
        ["toytype"] = "ToyType", ["minimumage"] = "MinimumAge", ["manufacturer"] = "Manufacturer"
    };

    private readonly LibraryContext _db;

    public ItemImportService(LibraryContext db) => _db = db;

    public string TemplateCsv() =>
        string.Join("\r\n",
            "Type,Name,Description,LibraryCode,BranchCode,Status,Author,Genre,PublicationYear,Artist,ReleaseYear,ToyType,MinimumAge,Manufacturer",
            "Book,Example Book,A short description,B101,CEN,Available,Jane Author,Fiction,2020,,,,,",
            "Music,Example Album,A short description,M101,CEN,Available,,Jazz,,Example Artist,2019,,,",
            "Toy,Example Toy,A short description,T101,CEN,Available,,,,,,Puzzle,5,Example Maker") + "\r\n";

    public async Task<ImportReport> ImportAsync(Stream csv, ImportOptions options, CancellationToken cancellationToken = default)
    {
        var report = new ImportReport { ValidateOnly = options.ValidateOnly, StopOnAnyError = options.StopOnAnyError };

        var text = await ReadTextAsync(csv, report, cancellationToken);
        if (text == null) return report;

        List<CsvRecord> records;
        try { records = CsvReader.Parse(text); }
        catch (CsvFormatException ex)
        {
            report.FileError = $"The file could not be read (line {ex.Line}): {ex.Message}";
            return report;
        }

        if (records.Count == 0)
        {
            report.FileError = "The file is empty. It needs a header row and at least one item.";
            return report;
        }

        var columns = ReadHeader(records[0], report);
        if (columns == null) return report;
        var headerWidth = records[0].Fields.Length;

        var dataRows = records.Skip(1).ToList();
        if (dataRows.Count == 0)
        {
            report.FileError = "The file has a header row but no items.";
            return report;
        }
        if (dataRows.Count > MaxRows)
        {
            report.FileError = $"The file has {dataRows.Count} rows. The limit is {MaxRows} per import; please split it.";
            return report;
        }

        var existingCodes = new HashSet<string>(
            await _db.Items.Select(i => i.LibraryCode).ToListAsync(cancellationToken),
            StringComparer.OrdinalIgnoreCase);
        var branchesByCode = (await _db.Branches.AsNoTracking().ToListAsync(cancellationToken))
            .GroupBy(b => b.Code.ToUpperInvariant())
            .ToDictionary(g => g.Key, g => g.First());
        var defaultBranch = await _db.Branches.AsNoTracking()
            .FirstOrDefaultAsync(b => b.Id == Branch.DefaultBranchId, cancellationToken);

        var seenInFile = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var toAdd = new List<(int RowIndex, Item Entity)>();

        foreach (var record in dataRows)
        {
            var messages = new List<string>();
            string Cell(string key) =>
                columns.TryGetValue(key, out var index) && index < record.Fields.Length ? record.Fields[index].Trim() : "";

            var typeText = Cell("type");
            var code = Cell("librarycode");

            if (record.Fields.Length > headerWidth)
                messages.Add($"This row has {record.Fields.Length} values but the header has {headerWidth} columns. Check for a comma inside a value that is not in quotes.");

            var entity = BuildItem(typeText, Cell, messages);

            // Common fields.
            if (entity != null)
            {
                entity.Name = Cell("name");
                entity.Description = Cell("description");
                entity.LibraryCode = code;
                entity.Status = ReadStatus(Cell("status"), messages);
                entity.BranchId = ReadBranch(Cell("branchcode"), branchesByCode, defaultBranch, messages);
            }

            if (entity != null)
            {
                var problems = new List<ValidationResult>();
                Validator.TryValidateObject(entity, new ValidationContext(entity), problems, validateAllProperties: true);
                foreach (var problem in problems.Where(p => !string.IsNullOrWhiteSpace(p.ErrorMessage)))
                {
                    if (!messages.Contains(problem.ErrorMessage!)) messages.Add(problem.ErrorMessage!);
                }
            }

            if (code.Length > 0)
            {
                if (seenInFile.TryGetValue(code, out var firstLine))
                    messages.Add($"The library code \"{code}\" is already used on line {firstLine} of this file.");
                else
                    seenInFile[code] = record.Line;

                if (existingCodes.Contains(code))
                    messages.Add($"An item with the library code \"{code}\" already exists in the catalogue.");
            }

            var status = messages.Count == 0 ? ImportRowStatus.ReadyToImport : ImportRowStatus.Invalid;
            report.Rows.Add(new ImportRowResult(record.Line, typeText, code, Cell("name"), status, messages));
            if (entity != null && messages.Count == 0) toAdd.Add((report.Rows.Count - 1, entity));
        }

        await SaveAsync(report, toAdd, options, cancellationToken);
        return report;
    }

    private async Task SaveAsync(ImportReport report, List<(int RowIndex, Item Entity)> toAdd,
        ImportOptions options, CancellationToken cancellationToken)
    {
        if (options.ValidateOnly || toAdd.Count == 0) return;

        if (options.StopOnAnyError && report.InvalidCount > 0)
        {
            report.NotImportedReason =
                $"Nothing was imported because {report.InvalidCount} row(s) have problems and \"stop if any row is invalid\" is on.";
            return;
        }

        _db.Items.AddRange(toAdd.Select(t => t.Entity));
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Most likely another Admin added one of these library codes after we checked.
            foreach (var entry in _db.ChangeTracker.Entries<Item>().ToList()) entry.State = EntityState.Detached;
            report.NotImportedReason =
                "Saving failed, so nothing was imported. A library code may have been added by someone else a moment ago; run the import again.";
            return;
        }

        foreach (var (rowIndex, _) in toAdd)
            report.Rows[rowIndex] = report.Rows[rowIndex] with { Status = ImportRowStatus.Imported };
    }

    private static async Task<string?> ReadTextAsync(Stream csv, ImportReport report, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        int read;
        while ((read = await csv.ReadAsync(chunk, cancellationToken)) > 0)
        {
            buffer.Write(chunk, 0, read);
            if (buffer.Length > MaxBytes)
            {
                report.FileError = $"The file is larger than {MaxBytes / 1024 / 1024} MB.";
                return null;
            }
        }

        try
        {
            // detectEncodingFromByteOrderMarks handles the BOM Excel adds to "CSV UTF-8" files.
            buffer.Position = 0;
            using var reader = new StreamReader(buffer, new UTF8Encoding(false, throwOnInvalidBytes: true), detectEncodingFromByteOrderMarks: true);
            return await reader.ReadToEndAsync(cancellationToken);
        }
        catch (DecoderFallbackException)
        {
            report.FileError = "The file is not UTF-8 text. In Excel, use Save As, then \"CSV UTF-8 (Comma delimited)\".";
            return null;
        }
    }

    // Maps normalised column name -> position. Names ignore case, spaces and underscores,
    // so "Library Code", "library_code" and "LibraryCode" are the same column.
    private static Dictionary<string, int>? ReadHeader(CsvRecord header, ImportReport report)
    {
        var columns = new Dictionary<string, int>();
        for (var i = 0; i < header.Fields.Length; i++)
        {
            var original = header.Fields[i].Trim();
            var key = new string(original.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
            if (key.Length == 0) continue;

            if (!ColumnTitles.ContainsKey(key))
            {
                report.Warnings.Add($"Column \"{original}\" is not recognised and was ignored.");
                continue;
            }
            if (!columns.TryAdd(key, i))
            {
                report.FileError = $"The column \"{ColumnTitles[key]}\" appears more than once in the header.";
                return null;
            }
        }

        var missing = RequiredColumns.Where(c => !columns.ContainsKey(c)).Select(c => ColumnTitles[c]).ToList();
        if (missing.Count > 0)
        {
            report.FileError = $"The header row is missing required column(s): {string.Join(", ", missing)}. Download the template for the expected layout.";
            return null;
        }
        return columns;
    }

    private static Item? BuildItem(string typeText, Func<string, string> cell, List<string> messages)
    {
        switch (typeText.ToLowerInvariant())
        {
            case "book":
                return new Book
                {
                    Author = cell("author"),
                    Genre = cell("genre"),
                    PublicationYear = ReadInt(cell("publicationyear"), "PublicationYear", messages)
                };
            case "music":
                return new Music
                {
                    Artist = cell("artist"),
                    Genre = cell("genre"),
                    ReleaseYear = ReadInt(cell("releaseyear"), "ReleaseYear", messages)
                };
            case "toy":
                return new Toy
                {
                    ToyType = cell("toytype"),
                    MinimumAge = ReadInt(cell("minimumage"), "MinimumAge", messages) ?? 0,
                    Manufacturer = cell("manufacturer")
                };
            case "":
                messages.Add("Type is required. Use Book, Music or Toy.");
                return null;
            default:
                messages.Add($"Type \"{typeText}\" is not valid. Use Book, Music or Toy.");
                return null;
        }
    }

    private static int? ReadInt(string value, string column, List<string> messages)
    {
        if (value.Length == 0) return null;
        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)) return number;
        messages.Add($"{column} must be a whole number (found \"{value}\").");
        return null;
    }

    private static ItemStatus ReadStatus(string value, List<string> messages)
    {
        if (value.Length == 0) return ItemStatus.Available;
        if (value.Equals("Available", StringComparison.OrdinalIgnoreCase)) return ItemStatus.Available;
        if (value.Equals("Damaged", StringComparison.OrdinalIgnoreCase)) return ItemStatus.Damaged;

        messages.Add($"Status \"{value}\" is not allowed. Use Available or Damaged (Borrowed, Destroy and In transit are set by the loan and transfer workflows).");
        return ItemStatus.Available;
    }

    private static int ReadBranch(string code, Dictionary<string, Branch> branchesByCode, Branch? defaultBranch, List<string> messages)
    {
        if (code.Length == 0)
        {
            // Blank means the default branch, as in the Section 1 integration notes.
            if (defaultBranch == null || !defaultBranch.IsActive)
                messages.Add("BranchCode is blank and the default branch is not available. Give a BranchCode.");
            return Branch.DefaultBranchId;
        }

        if (!branchesByCode.TryGetValue(code.ToUpperInvariant(), out var branch))
        {
            messages.Add($"BranchCode \"{code}\" does not match any branch.");
            return Branch.DefaultBranchId;
        }
        if (!branch.IsActive)
            messages.Add($"Branch \"{branch.Code}\" is not active, so items cannot be added to it.");
        return branch.Id;
    }
}
