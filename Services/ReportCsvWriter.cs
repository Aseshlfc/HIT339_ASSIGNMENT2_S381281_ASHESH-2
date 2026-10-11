using System.Text;

namespace LibraryManagementSystem.Services;

public static class ReportCsvWriter
{
    /// <summary>
    /// Renders a report as CSV (RFC 4180 quoting, CRLF line endings) with a
    /// UTF-8 byte order mark so Excel opens it with the right encoding.
    /// </summary>
    public static byte[] Write(ReportDocument report)
    {
        var sb = new StringBuilder();

        Line(sb, "Report", report.Title);
        Line(sb, "Generated", report.GeneratedAt.ToString("yyyy-MM-dd HH:mm"));
        foreach (var filter in report.Filters)
            Line(sb, filter.Key, filter.Value);
        sb.Append("\r\n");

        Line(sb, "Summary");
        Line(sb, "Metric", "Value");
        foreach (var metric in report.Summary)
            sb.Append(Escape(metric.Label)).Append(',').Append(Escape(ReportFormat.Csv(metric.Value, metric.Kind), isText: false)).Append("\r\n");

        foreach (var table in report.Tables)
        {
            sb.Append("\r\n");
            Line(sb, table.Title);
            sb.Append(string.Join(",", table.Columns.Select(c => Escape(c.Header)))).Append("\r\n");
            if (table.Rows.Count == 0)
            {
                Line(sb, table.EmptyMessage);
                continue;
            }
            foreach (var row in table.Rows)
            {
                var cells = row.Select((value, i) =>
                {
                    var kind = table.Columns[i].Kind;
                    return Escape(ReportFormat.Csv(value, kind), isText: kind == ReportValueKind.Text);
                });
                sb.Append(string.Join(",", cells)).Append("\r\n");
            }
        }

        var body = Encoding.UTF8.GetBytes(sb.ToString());
        return Encoding.UTF8.GetPreamble().Concat(body).ToArray();
    }

    /// <summary>
    /// Quotes a value when it contains a comma, quote or line break. Free-text
    /// values that start with a formula character are prefixed with an
    /// apostrophe so a spreadsheet does not execute them (CSV injection).
    /// </summary>
    public static string Escape(string? value, bool isText = true)
    {
        value ??= string.Empty;
        if (isText && value.Length > 0 && "=+-@\t\r".Contains(value[0]))
            value = "'" + value;
        if (value.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0)
            value = "\"" + value.Replace("\"", "\"\"") + "\"";
        return value;
    }

    private static void Line(StringBuilder sb, params string[] cells)
        => sb.Append(string.Join(",", cells.Select(c => Escape(c)))).Append("\r\n");
}
