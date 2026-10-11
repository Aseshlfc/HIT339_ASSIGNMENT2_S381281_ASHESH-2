using System.Globalization;

namespace LibraryManagementSystem.Services;

public enum ReportValueKind { Text, Number, Money, Date, Percent }

public record ReportColumn(string Header, ReportValueKind Kind = ReportValueKind.Text);

public record ReportMetric(string Label, object? Value, ReportValueKind Kind = ReportValueKind.Number, bool Warn = false);

public class ReportTable
{
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public List<ReportColumn> Columns { get; set; } = new();
    public List<object?[]> Rows { get; set; } = new();
    public string EmptyMessage { get; set; } = "No records match the selected filters.";
}

/// <summary>
/// A report in a presentation-neutral shape. The on-screen page, the CSV
/// export and the PDF export all render the same document, so the three can
/// never disagree about the numbers.
/// </summary>
public class ReportDocument
{
    public string Key { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public DateTime GeneratedAt { get; set; } = DateTime.Now;
    public List<KeyValuePair<string, string>> Filters { get; set; } = new();
    public List<ReportMetric> Summary { get; set; } = new();
    public List<ReportTable> Tables { get; set; } = new();
    public List<string> Notes { get; set; } = new();

    public string FileName(string extension)
        => $"{Key}-report-{GeneratedAt:yyyyMMdd-HHmm}.{extension}";
}

public class ReportFilter
{
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public int? BranchId { get; set; }
    public string? ItemType { get; set; }
    /// <summary>"Paid", "Outstanding" or empty for both. Fine audit only.</summary>
    public string? FineStatus { get; set; }
}

public static class ReportFormat
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>Human-readable value for the web page and the PDF.</summary>
    public static string Display(object? value, ReportValueKind kind)
    {
        if (value == null) return "—";
        return kind switch
        {
            ReportValueKind.Money => "$" + Convert.ToDecimal(value, Inv).ToString("N2", Inv),
            ReportValueKind.Percent => Convert.ToDecimal(value, Inv).ToString("0.0", Inv) + "%",
            ReportValueKind.Date => ((DateTime)value).ToString("dd MMM yyyy", Inv),
            ReportValueKind.Number => Convert.ToDecimal(value, Inv).ToString("#,0.##", Inv),
            _ => Convert.ToString(value, Inv) ?? string.Empty
        };
    }

    /// <summary>Machine-friendly value for CSV: no currency symbols or thousands separators.</summary>
    public static string Csv(object? value, ReportValueKind kind)
    {
        if (value == null) return string.Empty;
        return kind switch
        {
            ReportValueKind.Money => Convert.ToDecimal(value, Inv).ToString("0.00", Inv),
            ReportValueKind.Percent => Convert.ToDecimal(value, Inv).ToString("0.0", Inv),
            ReportValueKind.Date => ((DateTime)value).ToString("yyyy-MM-dd", Inv),
            ReportValueKind.Number => Convert.ToDecimal(value, Inv).ToString("0.##", Inv),
            _ => Convert.ToString(value, Inv) ?? string.Empty
        };
    }
}
