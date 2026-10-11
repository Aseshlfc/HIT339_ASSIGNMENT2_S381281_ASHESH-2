using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace LibraryManagementSystem.Services;

public interface IReportPdfService
{
    byte[] Render(ReportDocument report);
}

/// <summary>
/// Renders a report as an A4 landscape PDF using QuestPDF under its free
/// Community licence.
/// </summary>
public class ReportPdfService : IReportPdfService
{
    private const string Primary = "#7A1F2B";
    private const string Accent = "#A97E2F";
    private const string Muted = "#78695A";
    private const string Border = "#E7DDC9";
    private const string Warm = "#FAF5E9";
    private const string Danger = "#A3312A";

    static ReportPdfService()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public byte[] Render(ReportDocument report)
    {
        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4.Landscape());
                page.Margin(32);
                page.DefaultTextStyle(t => t.FontSize(9).FontColor("#2A2117"));

                page.Header().Element(c => Header(c, report));
                page.Content().PaddingVertical(12).Element(c => Content(c, report));
                page.Footer().Element(c => Footer(c, report));
            });
        }).GeneratePdf();
    }

    private static void Header(IContainer container, ReportDocument report)
    {
        container.BorderBottom(2).BorderColor(Accent).PaddingBottom(8).Row(row =>
        {
            row.RelativeItem().Column(col =>
            {
                col.Item().Text("Simple Library").FontSize(10).SemiBold().FontColor(Accent);
                col.Item().Text(report.Title).FontSize(18).Bold().FontColor(Primary);
            });
            row.ConstantItem(200).AlignRight().AlignBottom()
                .Text($"Generated {report.GeneratedAt:dd MMM yyyy, HH:mm}").FontColor(Muted);
        });
    }

    private static void Footer(IContainer container, ReportDocument report)
    {
        container.BorderTop(1).BorderColor(Border).PaddingTop(6).Row(row =>
        {
            row.RelativeItem().Text($"{report.Title} · Confidential: for library management use").FontSize(8).FontColor(Muted);
            row.ConstantItem(100).AlignRight().Text(text =>
            {
                text.DefaultTextStyle(t => t.FontSize(8).FontColor(Muted));
                text.Span("Page ");
                text.CurrentPageNumber();
                text.Span(" of ");
                text.TotalPages();
            });
        });
    }

    private static void Content(IContainer container, ReportDocument report)
    {
        container.Column(col =>
        {
            col.Spacing(12);

            col.Item().Text(report.Description).FontColor(Muted);

            col.Item().Background(Warm).Border(1).BorderColor(Border).Padding(8).Column(filters =>
            {
                filters.Item().Text("Report filters").SemiBold().FontColor(Primary);
                foreach (var filter in report.Filters)
                {
                    filters.Item().Text(text =>
                    {
                        text.Span(filter.Key + ": ").SemiBold();
                        text.Span(filter.Value);
                    });
                }
            });

            col.Item().Element(c => SummaryGrid(c, report));

            foreach (var table in report.Tables)
                col.Item().Element(c => Table(c, table));

            if (report.Notes.Count > 0)
            {
                col.Item().ShowEntire().Column(notes =>
                {
                    notes.Item().Text("Notes").SemiBold().FontColor(Primary);
                    foreach (var note in report.Notes)
                        notes.Item().Text("• " + note).FontSize(8).FontColor(Muted);
                });
            }
        });
    }

    private static void SummaryGrid(IContainer container, ReportDocument report)
    {
        const int perRow = 5;
        container.ShowEntire().Column(col =>
        {
            col.Spacing(6);
            col.Item().Text("Summary").FontSize(12).Bold().FontColor(Primary);
            foreach (var chunk in report.Summary.Chunk(perRow))
            {
                col.Item().Row(row =>
                {
                    row.Spacing(6);
                    foreach (var metric in chunk)
                    {
                        row.RelativeItem().Background(Warm).BorderLeft(3).BorderColor(metric.Warn ? Danger : Accent)
                            .Padding(7).Column(card =>
                            {
                                card.Item().Text(metric.Label.ToUpperInvariant()).FontSize(7).FontColor(Muted);
                                card.Item().Text(ReportFormat.Display(metric.Value, metric.Kind))
                                    .FontSize(14).Bold().FontColor(metric.Warn ? Danger : Primary);
                            });
                    }
                    // Keep the cards the same width on a short last row.
                    for (var i = chunk.Length; i < perRow; i++)
                        row.RelativeItem();
                });
            }
        });
    }

    private static void Table(IContainer container, ReportTable table)
    {
        // A Decoration keeps the heading attached to its table: the heading is
        // never left alone at the bottom of a page, and it is repeated when a
        // long table continues on the next page.
        container.Decoration(section =>
        {
            section.Before().PaddingBottom(4).Column(heading =>
            {
                heading.Item().Text(table.Title).FontSize(12).Bold().FontColor(Primary);
                if (!string.IsNullOrWhiteSpace(table.Description))
                    heading.Item().Text(table.Description).FontSize(8).FontColor(Muted);
            });

            if (table.Rows.Count == 0)
            {
                section.Content().ExtendHorizontal().Background(Warm).Border(1).BorderColor(Border).Padding(10)
                    .Text(table.EmptyMessage).Italic().FontColor(Muted);
                return;
            }

            section.Content().Table(t =>
            {
                t.ColumnsDefinition(columns =>
                {
                    foreach (var column in table.Columns)
                    {
                        columns.RelativeColumn(column.Kind switch
                        {
                            ReportValueKind.Text => 3f,
                            ReportValueKind.Date => 2.2f,
                            ReportValueKind.Number => 1.5f,
                            _ => 1.8f
                        });
                    }
                });

                // The header row repeats at the top of every page the table spans.
                t.Header(header =>
                {
                    foreach (var column in table.Columns)
                    {
                        var cell = header.Cell().Background(Primary).PaddingVertical(5).PaddingHorizontal(6);
                        Align(cell, column.Kind).Text(column.Header).FontSize(8).SemiBold().FontColor(Colors.White);
                    }
                });

                var index = 0;
                foreach (var row in table.Rows)
                {
                    var background = index++ % 2 == 0 ? "#FFFFFF" : Warm;
                    for (var i = 0; i < table.Columns.Count; i++)
                    {
                        var kind = table.Columns[i].Kind;
                        // ShowEntire keeps a row's wrapped text together instead of
                        // splitting one row across two pages.
                        var cell = t.Cell().Background(background).BorderBottom(1).BorderColor(Border)
                            .PaddingVertical(4).PaddingHorizontal(6).ShowEntire();
                        Align(cell, kind).Text(ReportFormat.Display(row[i], kind));
                    }
                }
            });
        });
    }

    private static IContainer Align(IContainer cell, ReportValueKind kind)
        => kind is ReportValueKind.Number or ReportValueKind.Money or ReportValueKind.Percent ? cell.AlignRight() : cell;
}
