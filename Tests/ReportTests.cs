using System.Text;
using LibraryManagementSystem.Controllers;
using LibraryManagementSystem.Data;
using LibraryManagementSystem.Models;
using LibraryManagementSystem.Services;
using LibraryManagementSystem.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Reflection;
using UglyToad.PdfPig;

namespace LibraryManagementSystem.Tests;

internal static class ReportData
{
    public static readonly DateTime Today = DateTime.Now.Date;

    /// <summary>
    /// Two branches with four items and five loans:
    ///   L1 Central book, active, not overdue
    ///   L2 Central book, active, 3 days overdue
    ///   L3 North music, returned 5 days late, fine 5.50 paid
    ///   L4 North toy, returned 2 days late, fine 2.25 outstanding
    ///   L5 no branch recorded, returned on time, no fine
    /// </summary>
    public static async Task<(Branch central, Branch north)> SeedAsync(LibraryContext db)
    {
        var (central, north, _) = await Section1.SeedBranchesAsync(db);
        var alice = new Borrower { FullName = "Alice Nguyen", Email = "a@example.com", Phone = "1", Address = "1" };
        var ben = new Borrower { FullName = "Ben, \"The\" Carter", Email = "b@example.com", Phone = "2", Address = "2" };
        db.Borrowers.AddRange(alice, ben);

        var hobbit = new Book { Name = "The Hobbit", Description = "d", LibraryCode = "B001", Author = "T", Genre = "F", BranchId = central.Id, Status = ItemStatus.Borrowed };
        var dune = new Book { Name = "Dune", Description = "d", LibraryCode = "B002", Author = "H", Genre = "S", BranchId = central.Id, Status = ItemStatus.Borrowed };
        var thriller = new Music { Name = "Thriller", Description = "d", LibraryCode = "M001", Artist = "MJ", BranchId = north.Id, Status = ItemStatus.Available };
        var chess = new Toy { Name = "=Chess Set", Description = "d", LibraryCode = "T001", ToyType = "Game", BranchId = north.Id, Status = ItemStatus.Damaged };
        db.Items.AddRange(hobbit, dune, thriller, chess);
        await db.SaveChangesAsync();

        db.Loans.AddRange(
            new Loan { ItemId = hobbit.Id, BorrowerId = alice.Id, BranchId = central.Id, BorrowedAt = Today.AddDays(-5), DueDate = Today.AddDays(9) },
            new Loan { ItemId = dune.Id, BorrowerId = ben.Id, BranchId = central.Id, BorrowedAt = Today.AddDays(-17), DueDate = Today.AddDays(-3) },
            new Loan { ItemId = thriller.Id, BorrowerId = alice.Id, BranchId = north.Id, BorrowedAt = Today.AddDays(-40), DueDate = Today.AddDays(-26), ReturnedAt = Today.AddDays(-21), FineAmount = 5.50m, FinePaid = true },
            new Loan { ItemId = chess.Id, BorrowerId = ben.Id, BranchId = north.Id, BorrowedAt = Today.AddDays(-30), DueDate = Today.AddDays(-16), ReturnedAt = Today.AddDays(-14), FineAmount = 2.25m, FinePaid = false },
            new Loan { ItemId = thriller.Id, BorrowerId = ben.Id, BranchId = null, BorrowedAt = Today.AddDays(-90), DueDate = Today.AddDays(-76), ReturnedAt = Today.AddDays(-80) }
        );
        await db.SaveChangesAsync();
        return (central, north);
    }

    public static object? Metric(ReportDocument report, string label)
        => report.Summary.Single(m => m.Label == label).Value;

    public static ReportTable Table(ReportDocument report, string title)
        => report.Tables.Single(t => t.Title == title);
}

public class ReportServiceTests
{
    [Fact]
    public async Task Borrowing_Unfiltered_CountsEveryLoanByStatus()
    {
        using var db = TestDb.Create();
        await ReportData.SeedAsync(db);

        var report = await new ReportService(db).BuildBorrowingReportAsync(new ReportFilter());

        Assert.Equal(5, ReportData.Metric(report, "Total loans"));
        Assert.Equal(2, ReportData.Metric(report, "Active loans"));
        Assert.Equal(3, ReportData.Metric(report, "Returned loans"));
        Assert.Equal(1, ReportData.Metric(report, "Overdue loans"));
        Assert.Equal(2, ReportData.Metric(report, "Returned late"));
        Assert.Equal(5, ReportData.Table(report, "Loan details").Rows.Count);
    }

    [Fact]
    public async Task Borrowing_ByCategory_AndPopularItems_AreCorrect()
    {
        using var db = TestDb.Create();
        await ReportData.SeedAsync(db);

        var report = await new ReportService(db).BuildBorrowingReportAsync(new ReportFilter());

        var byCategory = ReportData.Table(report, "Borrowing by item category").Rows.ToDictionary(r => (string)r[0]!, r => (int)r[1]!);
        Assert.Equal(2, byCategory["Book"]);
        Assert.Equal(2, byCategory["Music"]);
        Assert.Equal(1, byCategory["Toy"]);

        var top = ReportData.Table(report, "Most borrowed items (top 10)").Rows[0];
        Assert.Equal("Thriller", top[0]);
        Assert.Equal(2, top[3]);
    }

    [Fact]
    public async Task Borrowing_ByBranch_KeepsLoansWithoutARecordedBranchSeparate()
    {
        using var db = TestDb.Create();
        await ReportData.SeedAsync(db);

        var report = await new ReportService(db).BuildBorrowingReportAsync(new ReportFilter());

        var byBranch = ReportData.Table(report, "Borrowing by issuing branch").Rows.ToDictionary(r => (string)r[0]!, r => (int)r[1]!);
        Assert.Equal(2, byBranch["Central Library"]);
        Assert.Equal(2, byBranch["Northside Branch"]);
        Assert.Equal(1, byBranch[ReportService.NoBranchLabel]);
    }

    [Fact]
    public async Task Borrowing_BranchFilter_OnlyCountsLoansIssuedAtThatBranch()
    {
        using var db = TestDb.Create();
        var (_, north) = await ReportData.SeedAsync(db);

        var report = await new ReportService(db).BuildBorrowingReportAsync(new ReportFilter { BranchId = north.Id });

        Assert.Equal(2, ReportData.Metric(report, "Total loans"));
        Assert.Contains(report.Filters, f => f.Key == "Branch" && f.Value == "Northside Branch");
    }

    [Fact]
    public async Task Borrowing_DateRange_IsInclusiveOnBothEnds()
    {
        using var db = TestDb.Create();
        await ReportData.SeedAsync(db);
        var filter = new ReportFilter { From = ReportData.Today.AddDays(-30), To = ReportData.Today.AddDays(-17) };

        var report = await new ReportService(db).BuildBorrowingReportAsync(filter);

        // Borrowed 30 and 17 days ago are both on the boundary and included.
        Assert.Equal(2, ReportData.Metric(report, "Total loans"));
    }

    [Fact]
    public async Task Borrowing_CategoryFilter_AndCombinedFilters()
    {
        using var db = TestDb.Create();
        var (central, _) = await ReportData.SeedAsync(db);
        var service = new ReportService(db);

        var music = await service.BuildBorrowingReportAsync(new ReportFilter { ItemType = "Music" });
        var musicAtCentral = await service.BuildBorrowingReportAsync(new ReportFilter { ItemType = "Music", BranchId = central.Id });

        Assert.Equal(2, ReportData.Metric(music, "Total loans"));
        Assert.Equal(0, ReportData.Metric(musicAtCentral, "Total loans"));
    }

    [Fact]
    public async Task FineAudit_Totals_SplitPaidAndOutstanding()
    {
        using var db = TestDb.Create();
        await ReportData.SeedAsync(db);

        var report = await new ReportService(db).BuildFineAuditReportAsync(new ReportFilter());

        Assert.Equal(7.75m, ReportData.Metric(report, "Fines assessed"));
        Assert.Equal(5.50m, ReportData.Metric(report, "Marked as paid"));
        Assert.Equal(2.25m, ReportData.Metric(report, "Outstanding"));
        Assert.Equal(2, ReportData.Metric(report, "Fined loans"));
        Assert.Equal(71.0m, ReportData.Metric(report, "Collection rate"));
        // One loan is 3 days overdue and not yet returned.
        Assert.Equal(3 * LibraryRules.FinePerLateDay, ReportData.Metric(report, "Accruing on overdue loans"));
    }

    [Fact]
    public async Task FineAudit_DetailRows_ShowDaysLateBorrowerAndItem()
    {
        using var db = TestDb.Create();
        await ReportData.SeedAsync(db);

        var report = await new ReportService(db).BuildFineAuditReportAsync(new ReportFilter());

        var rows = ReportData.Table(report, "Fined loans").Rows;
        var paid = rows.Single(r => (string)r[9]! == "Paid");
        Assert.Equal("Thriller", paid[1]);
        Assert.Equal("Alice Nguyen", paid[3]);
        Assert.Equal(5, paid[7]);
        Assert.Equal(5.50m, paid[8]);
    }

    [Fact]
    public async Task FineAudit_StatusAndBranchFilters()
    {
        using var db = TestDb.Create();
        var (central, _) = await ReportData.SeedAsync(db);
        var service = new ReportService(db);

        var outstanding = await service.BuildFineAuditReportAsync(new ReportFilter { FineStatus = "Outstanding" });
        var atCentral = await service.BuildFineAuditReportAsync(new ReportFilter { BranchId = central.Id });

        Assert.Equal(2.25m, ReportData.Metric(outstanding, "Fines assessed"));
        Assert.Equal(0m, ReportData.Metric(outstanding, "Marked as paid"));
        Assert.Equal(0m, ReportData.Metric(atCentral, "Fines assessed"));
        Assert.Null(ReportData.Metric(atCentral, "Collection rate"));
    }

    [Fact]
    public async Task FineAudit_DateRange_AppliesToReturnDate()
    {
        using var db = TestDb.Create();
        await ReportData.SeedAsync(db);
        var filter = new ReportFilter { From = ReportData.Today.AddDays(-15), To = ReportData.Today };

        var report = await new ReportService(db).BuildFineAuditReportAsync(filter);

        // Only the fine assessed on the return 14 days ago falls in the range.
        Assert.Equal(2.25m, ReportData.Metric(report, "Fines assessed"));
    }

    [Fact]
    public async Task RealSqlite_FineTotals_AreExactDecimalsEvenThoughStoredAsText()
    {
        using var sqlite = new SqliteTestDb();
        using (var seed = sqlite.Open())
        {
            var (central, _, _) = await Section1.SeedBranchesAsync(seed);
            var book = await Section1.AddBookAsync(seed, "B1", central);
            var borrower = await Section1.AddBorrowerAsync(seed);
            // As text, "9.50" sorts above "10.00" and "0.10" + "0.20" is not a number:
            // these values only total correctly when handled as decimals.
            foreach (var (amount, paid) in new[] { (10.00m, true), (9.50m, false), (0.10m, false), (0.20m, true) })
            {
                seed.Loans.Add(new Loan
                {
                    ItemId = book.Id, BorrowerId = borrower.Id, BranchId = central.Id,
                    BorrowedAt = DateTime.Now.AddDays(-30), DueDate = DateTime.Now.AddDays(-16), ReturnedAt = DateTime.Now.AddDays(-10),
                    FineAmount = amount, FinePaid = paid
                });
            }
            await seed.SaveChangesAsync();
        }

        using var db = sqlite.Open();
        var report = await new ReportService(db).BuildFineAuditReportAsync(new ReportFilter());
        var dashboard = await new ReportService(db).BuildDashboardAsync();

        Assert.Equal(19.80m, ReportData.Metric(report, "Fines assessed"));
        Assert.Equal(10.20m, ReportData.Metric(report, "Marked as paid"));
        Assert.Equal(9.60m, ReportData.Metric(report, "Outstanding"));
        Assert.Equal(19.80m, dashboard.TotalFines);
        Assert.Equal(9.60m, dashboard.OutstandingFines);
    }

    [Fact]
    public async Task Inventory_Summary_ByBranch_AndByCategory()
    {
        using var db = TestDb.Create();
        await ReportData.SeedAsync(db);

        var report = await new ReportService(db).BuildInventoryHealthReportAsync(new ReportFilter());

        Assert.Equal(4, ReportData.Metric(report, "Total items"));
        Assert.Equal(1, ReportData.Metric(report, "Available"));
        Assert.Equal(2, ReportData.Metric(report, "Borrowed"));
        Assert.Equal(1, ReportData.Metric(report, "Damaged"));
        Assert.Equal(25.0m, ReportData.Metric(report, "Availability rate"));

        var byBranch = ReportData.Table(report, "Inventory by branch").Rows.ToDictionary(r => (string)r[0]!, r => r);
        Assert.Equal(2, byBranch["Central Library"][1]);
        Assert.Equal(2, byBranch["Northside Branch"][1]);
        Assert.Equal(0, byBranch["Riverside Branch"][1]); // empty branches are still listed
        Assert.Equal(1, byBranch["Northside Branch"][5]); // damaged

        var byCategory = ReportData.Table(report, "Inventory by category").Rows.ToDictionary(r => (string)r[0]!, r => (int)r[1]!);
        Assert.Equal(2, byCategory["Book"]);
    }

    [Fact]
    public async Task Inventory_BranchFilter_OnlyCountsThatBranch()
    {
        using var db = TestDb.Create();
        var (_, north) = await ReportData.SeedAsync(db);

        var report = await new ReportService(db).BuildInventoryHealthReportAsync(new ReportFilter { BranchId = north.Id });

        Assert.Equal(2, ReportData.Metric(report, "Total items"));
        Assert.Single(ReportData.Table(report, "Inventory by branch").Rows);
    }

    [Fact]
    public async Task Inventory_AttentionList_FlagsDamagedOverdueAndStatusLoanMismatches()
    {
        using var db = TestDb.Create();
        var (central, _) = await ReportData.SeedAsync(db);
        // Marked Borrowed by hand with no loan behind it.
        await Section1.AddBookAsync(db, "B099", central, ItemStatus.Borrowed, "Orphan");

        var report = await new ReportService(db).BuildInventoryHealthReportAsync(new ReportFilter());

        var issues = ReportData.Table(report, "Items needing attention").Rows.ToDictionary(r => (string)r[0]!, r => (string)r[5]!);
        Assert.Contains("Damaged", issues["T001"]);
        Assert.Contains("3 day(s) overdue", issues["B002"]);
        Assert.Contains("no open loan", issues["B099"]);
        Assert.False(issues.ContainsKey("B001")); // on loan and not overdue: nothing to flag
    }

    [Fact]
    public async Task EmptyDatabase_AllReportsBuildWithZeroTotals()
    {
        using var db = TestDb.Create();
        var service = new ReportService(db);

        var borrowing = await service.BuildBorrowingReportAsync(new ReportFilter());
        var fines = await service.BuildFineAuditReportAsync(new ReportFilter());
        var inventory = await service.BuildInventoryHealthReportAsync(new ReportFilter());
        var dashboard = await service.BuildDashboardAsync();

        Assert.Equal(0, ReportData.Metric(borrowing, "Total loans"));
        Assert.Equal(0m, ReportData.Metric(fines, "Fines assessed"));
        Assert.Equal(0, ReportData.Metric(inventory, "Total items"));
        Assert.Null(ReportData.Metric(inventory, "Availability rate"));
        Assert.Equal(0, dashboard.TotalItems);
        Assert.All(borrowing.Tables, t => Assert.Empty(t.Rows));
    }

    [Fact]
    public async Task Dashboard_ShowsPerBranchInventoryAndLoans()
    {
        using var db = TestDb.Create();
        var (central, north) = await ReportData.SeedAsync(db);

        var dashboard = await new ReportService(db).BuildDashboardAsync();

        Assert.Equal(4, dashboard.TotalItems);
        Assert.Equal(5, dashboard.TotalLoans);
        Assert.Equal(1, dashboard.LoansWithoutBranch);
        var centralRow = dashboard.Branches.Single(b => b.BranchId == central.Id);
        Assert.Equal(2, centralRow.Items);
        Assert.Equal(2, centralRow.Borrowed);
        Assert.Equal(2, centralRow.LoansIssued);
        Assert.Equal(1, centralRow.OverdueLoans);
        Assert.Equal(1, dashboard.Branches.Single(b => b.BranchId == north.Id).OutOfService);
        Assert.Equal("Thriller", dashboard.PopularItems[0].Name);
    }
}

public class ReportExportTests
{
    private static string CsvText(byte[] bytes) => Encoding.UTF8.GetString(bytes);

    private static ManagerController Controller(LibraryContext db)
    {
        var controller = new ManagerController(db, new ReportService(db), new ReportPdfService());
        Section1.SignIn(controller, "manager", "Manager");
        return controller;
    }

    private static string PdfText(byte[] bytes)
    {
        using var pdf = PdfDocument.Open(bytes);
        return string.Join(" ", pdf.GetPages().Select(p => p.Text));
    }

    [Fact]
    public void ManagerReports_AreRestrictedToManagerRole()
    {
        Assert.Equal("Manager", typeof(ManagerController).GetCustomAttribute<AuthorizeAttribute>()!.Roles);
        // No action opts out of the class-level restriction.
        Assert.DoesNotContain(typeof(ManagerController).GetMethods(), m => m.GetCustomAttribute<AllowAnonymousAttribute>() != null);
    }

    [Theory]
    [InlineData("plain", "plain")]
    [InlineData("a,b", "\"a,b\"")]
    [InlineData("say \"hi\"", "\"say \"\"hi\"\"\"")]
    [InlineData("line1\nline2", "\"line1\nline2\"")]
    [InlineData("=SUM(A1)", "'=SUM(A1)")]
    [InlineData("+61 400", "'+61 400")]
    [InlineData("@cmd", "'@cmd")]
    [InlineData("-2+3", "'-2+3")]
    public void CsvEscape_QuotesSpecialCharactersAndNeutralisesFormulas(string input, string expected)
    {
        Assert.Equal(expected, ReportCsvWriter.Escape(input));
    }

    [Fact]
    public void CsvEscape_LeavesNegativeNumbersAlone()
    {
        Assert.Equal("-5.00", ReportCsvWriter.Escape("-5.00", isText: false));
    }

    [Fact]
    public async Task Csv_Borrowing_HasBomHeadersFiltersAndEscapedData()
    {
        using var db = TestDb.Create();
        await ReportData.SeedAsync(db);
        var report = await new ReportService(db).BuildBorrowingReportAsync(new ReportFilter());

        var bytes = ReportCsvWriter.Write(report);
        var csv = CsvText(bytes);

        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes.Take(3));
        Assert.Contains("Report,Borrowing Statistics Report\r\n", csv);
        Assert.Contains("Branch,All branches\r\n", csv);
        Assert.Contains("Total loans,5\r\n", csv);
        Assert.Contains("Loan #,Borrowed,Item,Library code,Category,Borrower,Issuing branch,Due,Returned,Status\r\n", csv);
        // A borrower name containing a comma and quotes stays in one cell.
        Assert.Contains("\"Ben, \"\"The\"\" Carter\"", csv);
        // An item name starting with "=" cannot run as a spreadsheet formula.
        Assert.Contains("'=Chess Set", csv);
        Assert.DoesNotContain(",=Chess Set", csv);
        // Dates are ISO formatted.
        Assert.Contains(ReportData.Today.AddDays(-5).ToString("yyyy-MM-dd"), csv);
    }

    [Fact]
    public async Task Csv_FineAudit_UsesPlainDecimalAmounts()
    {
        using var db = TestDb.Create();
        await ReportData.SeedAsync(db);
        var report = await new ReportService(db).BuildFineAuditReportAsync(new ReportFilter());

        var csv = CsvText(ReportCsvWriter.Write(report));

        Assert.Contains("Fines assessed,7.75\r\n", csv);
        Assert.Contains("Marked as paid,5.50\r\n", csv);
        Assert.Contains("Outstanding,2.25\r\n", csv);
        Assert.DoesNotContain("$", csv.Replace("at $1.00", ""));
        Assert.Contains(",5.50,Paid\r\n", csv);
    }

    [Fact]
    public async Task Csv_RespectsFilters()
    {
        using var db = TestDb.Create();
        var (_, north) = await ReportData.SeedAsync(db);
        var report = await new ReportService(db).BuildInventoryHealthReportAsync(new ReportFilter { BranchId = north.Id });

        var csv = CsvText(ReportCsvWriter.Write(report));

        Assert.Contains("Branch,Northside Branch\r\n", csv);
        Assert.Contains("Total items,2\r\n", csv);
        Assert.Contains("M001,Thriller", csv);
        Assert.DoesNotContain("B001", csv);
    }

    [Fact]
    public async Task Csv_EmptyDataset_StillHasHeadersAndAnEmptyMessage()
    {
        using var db = TestDb.Create();
        var report = await new ReportService(db).BuildFineAuditReportAsync(new ReportFilter());

        var csv = CsvText(ReportCsvWriter.Write(report));

        Assert.Contains("Fines assessed,0.00\r\n", csv);
        Assert.Contains("Loan #,Item,Library code,Borrower,Issuing branch,Due,Returned,Days late,Fine,Status\r\n", csv);
        Assert.Contains("No fined loans match the selected filters.", csv);
    }

    [Fact]
    public async Task Pdf_EachReport_IsAValidPdfContainingItsHeadingFiltersAndData()
    {
        using var db = TestDb.Create();
        await ReportData.SeedAsync(db);
        var service = new ReportService(db);
        var pdf = new ReportPdfService();

        var borrowing = PdfText(pdf.Render(await service.BuildBorrowingReportAsync(new ReportFilter())));
        var fines = PdfText(pdf.Render(await service.BuildFineAuditReportAsync(new ReportFilter())));
        var inventory = PdfText(pdf.Render(await service.BuildInventoryHealthReportAsync(new ReportFilter())));

        Assert.Contains("Borrowing Statistics Report", borrowing);
        Assert.Contains("All branches", borrowing);
        Assert.Contains("The Hobbit", borrowing);
        Assert.Contains("Generated", borrowing);

        Assert.Contains("Fine Revenue Audit", fines);
        Assert.Contains("$7.75", fines);
        Assert.Contains("$2.25", fines);

        Assert.Contains("Inventory Health Report", inventory);
        Assert.Contains("Northside Branch", inventory);
    }

    [Fact]
    public async Task Pdf_StartsWithPdfSignature_AndEmptyDatasetStillRenders()
    {
        using var db = TestDb.Create();
        var report = await new ReportService(db).BuildBorrowingReportAsync(new ReportFilter());

        var bytes = new ReportPdfService().Render(report);

        Assert.Equal("%PDF-", Encoding.ASCII.GetString(bytes, 0, 5));
        Assert.Contains("No records match the selected filters.", PdfText(bytes));
    }

    [Fact]
    public async Task Pdf_LongReport_SpansSeveralPages()
    {
        using var db = TestDb.Create();
        var (central, _, _) = await Section1.SeedBranchesAsync(db);
        for (var i = 0; i < 150; i++)
            db.Items.Add(new Book { Name = $"Book {i}", Description = "d", LibraryCode = $"X{i:000}", Author = "A", Genre = "G", BranchId = central.Id });
        await db.SaveChangesAsync();
        var report = await new ReportService(db).BuildInventoryHealthReportAsync(new ReportFilter());

        using var pdf = PdfDocument.Open(new ReportPdfService().Render(report));

        Assert.True(pdf.NumberOfPages > 2);
        Assert.Contains("X149", pdf.GetPage(pdf.NumberOfPages).Text + pdf.GetPage(pdf.NumberOfPages - 1).Text);
    }

    [Theory]
    [InlineData("borrowing", "csv", "text/csv", "borrowing-report-")]
    [InlineData("fines", "csv", "text/csv", "fine-revenue-audit-report-")]
    [InlineData("inventory", "csv", "text/csv", "inventory-health-report-")]
    [InlineData("borrowing", "pdf", "application/pdf", "borrowing-report-")]
    [InlineData("fines", "pdf", "application/pdf", "fine-revenue-audit-report-")]
    [InlineData("inventory", "pdf", "application/pdf", "inventory-health-report-")]
    public async Task Export_ReturnsADownloadWithTheRightTypeAndFileName(string report, string format, string contentType, string filePrefix)
    {
        using var db = TestDb.Create();
        await ReportData.SeedAsync(db);

        var result = await Controller(db).Export(report, format, new ReportFilter());

        var file = Assert.IsType<FileContentResult>(result);
        Assert.Equal(contentType, file.ContentType);
        Assert.StartsWith(filePrefix, file.FileDownloadName);
        Assert.EndsWith("." + format, file.FileDownloadName);
        Assert.NotEmpty(file.FileContents);
    }

    [Fact]
    public async Task Export_AppliesTheSameFiltersAsThePage()
    {
        using var db = TestDb.Create();
        var (_, north) = await ReportData.SeedAsync(db);
        var controller = Controller(db);
        var filter = new ReportFilter { BranchId = north.Id, ItemType = "Toy" };

        var page = (ReportPageViewModel)((ViewResult)await controller.BorrowingReport(filter)).Model!;
        var file = (FileContentResult)await controller.Export("borrowing", "csv", filter);

        Assert.Equal(1, ReportData.Metric(page.Document, "Total loans"));
        var csv = CsvText(file.FileContents);
        Assert.Contains("Total loans,1\r\n", csv);
        Assert.Contains("Item category,Toy\r\n", csv);
    }

    [Fact]
    public async Task Export_UnknownReportOrFormat_IsRefused()
    {
        using var db = TestDb.Create();
        var controller = Controller(db);

        Assert.IsType<NotFoundResult>(await controller.Export("secrets", "csv", new ReportFilter()));
        Assert.IsType<BadRequestObjectResult>(await controller.Export("borrowing", "xlsx", new ReportFilter()));
    }

    [Fact]
    public async Task ReportPage_UnknownFilterValuesFromAHandEditedUrl_AreIgnored()
    {
        using var db = TestDb.Create();
        await ReportData.SeedAsync(db);
        var filter = new ReportFilter { BranchId = 9999, ItemType = "<script>", FineStatus = "=cmd" };

        var page = (ReportPageViewModel)((ViewResult)await Controller(db).FineAudit(filter)).Model!;

        Assert.Equal(7.75m, ReportData.Metric(page.Document, "Fines assessed"));
        Assert.DoesNotContain(page.Document.Filters, f => f.Value.Contains("script") || f.Value.Contains("cmd") || f.Value.Contains("9999"));
    }

    [Fact]
    public async Task ReportPage_BranchAndDateFiltersCombine()
    {
        using var db = TestDb.Create();
        var (_, north) = await ReportData.SeedAsync(db);
        var filter = new ReportFilter { BranchId = north.Id, From = ReportData.Today.AddDays(-35), To = ReportData.Today };

        var page = (ReportPageViewModel)((ViewResult)await Controller(db).BorrowingReport(filter)).Model!;

        // North has loans borrowed 40 and 30 days ago; only the second is in range.
        Assert.Equal(1, ReportData.Metric(page.Document, "Total loans"));
    }

    [Fact]
    public async Task ReportPage_ReversedDateRange_IsTreatedAsTheIntendedRange()
    {
        using var db = TestDb.Create();
        await ReportData.SeedAsync(db);
        var filter = new ReportFilter { From = ReportData.Today, To = ReportData.Today.AddDays(-10) };

        var page = (ReportPageViewModel)((ViewResult)await Controller(db).BorrowingReport(filter)).Model!;

        Assert.Equal(1, ReportData.Metric(page.Document, "Total loans"));
    }
}
