using System.Text;
using LibraryManagementSystem.Data;
using LibraryManagementSystem.Models;
using LibraryManagementSystem.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace LibraryManagementSystem.Tests;

public class CsvReaderTests
{
    [Fact]
    public void ReadsPlainRows_WithAnyLineEnding()
    {
        foreach (var eol in new[] { "\n", "\r\n", "\r" })
        {
            var records = CsvReader.Parse($"a,b,c{eol}1,2,3{eol}");
            Assert.Equal(2, records.Count);
            Assert.Equal(new[] { "1", "2", "3" }, records[1].Fields);
            Assert.Equal(2, records[1].Line);
        }
    }

    [Fact]
    public void QuotedFields_CanHoldCommasQuotesAndLineBreaks()
    {
        var records = CsvReader.Parse("a,b\n\"x, y\",\"say \"\"hi\"\"\"\n\"two\nlines\",z\nlast,row");

        Assert.Equal(new[] { "x, y", "say \"hi\"" }, records[1].Fields);
        Assert.Equal(new[] { "two\nlines", "z" }, records[2].Fields);
        Assert.Equal(3, records[2].Line);       // starts on line 3
        Assert.Equal(5, records[3].Line);       // the line break inside the quotes was counted
    }

    [Fact]
    public void BlankLinesAreSkipped_ButRowsOfEmptyCellsAreNot()
    {
        var records = CsvReader.Parse("a,b\n\n,\n   \n\"\",\n");

        Assert.Equal(4, records.Count);          // header, ",", "   ", "\"\","
        Assert.Equal(new[] { "", "" }, records[1].Fields);
    }

    [Fact]
    public void ByteOrderMarkIsIgnored_AndAMissingFinalLineBreakIsFine()
    {
        var records = CsvReader.Parse("﻿Name,Code\nA,1");
        Assert.Equal("Name", records[0].Fields[0]);
        Assert.Equal(2, records.Count);
    }

    [Fact]
    public void AnUnclosedQuote_IsReportedWithItsLine()
    {
        var error = Assert.Throws<CsvFormatException>(() => CsvReader.Parse("a,b\nx,\"never closed\ny,z"));
        Assert.Equal(2, error.Line);
    }
}

public class ItemImportServiceTests
{
    private const string Header = "Type,Name,Description,LibraryCode,BranchCode,Status,Author,Genre,PublicationYear,Artist,ReleaseYear,ToyType,MinimumAge,Manufacturer";

    private static Task<ImportReport> Run(LibraryContext db, string csv, bool validateOnly = false, bool stopOnAnyError = false) =>
        new ItemImportService(db).ImportAsync(new MemoryStream(Encoding.UTF8.GetBytes(csv)), new ImportOptions(validateOnly, stopOnAnyError));

    private static string File(params string[] rows) => Header + "\r\n" + string.Join("\r\n", rows) + "\r\n";

    private const string GoodBook = "Book,Emma,A comedy.,B900,CEN,Available,Jane Austen,Romance,1815,,,,,";

    [Fact]
    public async Task TheDownloadableTemplate_ImportsCleanly()
    {
        using var testDb = new SqliteTestDb();
        using var db = testDb.Open();
        var service = new ItemImportService(db);

        var report = await service.ImportAsync(new MemoryStream(Encoding.UTF8.GetBytes(service.TemplateCsv())), new ImportOptions());

        Assert.True(report.FileAccepted, report.FileError);
        Assert.Equal(3, report.ImportedCount);
        Assert.Equal(0, report.InvalidCount);
        Assert.Equal(1, await db.Items.OfType<Book>().CountAsync());
        Assert.Equal(1, await db.Items.OfType<Music>().CountAsync());
        Assert.Equal(1, await db.Items.OfType<Toy>().CountAsync());
    }

    [Fact]
    public async Task ValidRows_AreSavedWithTheirTypeSpecificFieldsAndBranch()
    {
        using var testDb = new SqliteTestDb();
        using var db = testDb.Open();
        var (_, north, _) = await Section1.SeedBranchesAsync(db);

        var report = await Run(db, File(
            "Book,Emma,A comedy.,B900,NTH,Available,Jane Austen,Romance,1815,,,,,",
            "Music,Blue,An album.,M900,,damaged,,Folk,,Joni Mitchell,1971,,,",
            "toy,Rocket,\"Fast, loud\",T900,CEN,,,,,,,Construction,6,Acme"));

        Assert.True(report.FileAccepted, report.FileError);
        Assert.Equal(3, report.ImportedCount);

        var book = await db.Items.OfType<Book>().SingleAsync();
        Assert.Equal("Jane Austen", book.Author);
        Assert.Equal(1815, book.PublicationYear);
        Assert.Equal(north.Id, book.BranchId);
        Assert.Equal(ItemStatus.Available, book.Status);

        var music = await db.Items.OfType<Music>().SingleAsync();
        Assert.Equal("Joni Mitchell", music.Artist);
        Assert.Equal(ItemStatus.Damaged, music.Status);
        Assert.Equal(Branch.DefaultBranchId, music.BranchId);          // blank branch = default branch

        var toy = await db.Items.OfType<Toy>().SingleAsync();
        Assert.Equal("Fast, loud", toy.Description);                    // comma inside quotes survives
        Assert.Equal(6, toy.MinimumAge);
        Assert.Equal(Branch.DefaultBranchId, toy.BranchId);
    }

    [Fact]
    public async Task ValidateOnly_ReportsButSavesNothing()
    {
        using var testDb = new SqliteTestDb();
        using var db = testDb.Open();
        await Section1.SeedBranchesAsync(db);

        var report = await Run(db, File(GoodBook), validateOnly: true);

        Assert.True(report.ValidateOnly);
        Assert.Equal(1, report.ReadyCount);
        Assert.Equal(0, report.ImportedCount);
        Assert.Equal(0, await db.Items.CountAsync());
    }

    [Fact]
    public async Task InvalidRows_AreSkipped_AndTheValidOnesStillImport()
    {
        using var testDb = new SqliteTestDb();
        using var db = testDb.Open();
        await Section1.SeedBranchesAsync(db);

        var report = await Run(db, File(
            GoodBook,
            "Book,No Author,d,B901,CEN,Available,,Romance,,,,,,",
            "Book,Good Two,d,B902,CEN,Available,Someone,Mystery,,,,,,"));

        Assert.Equal(2, report.ImportedCount);
        Assert.Equal(1, report.InvalidCount);
        var bad = report.Rows.Single(r => r.Status == ImportRowStatus.Invalid);
        Assert.Equal(3, bad.Line);                                      // header is line 1
        Assert.Equal("B901", bad.LibraryCode);
        Assert.Contains("Author is required.", bad.Messages);
        Assert.Equal(2, await db.Items.CountAsync());
    }

    [Fact]
    public async Task StopOnAnyError_ImportsNothingWhenOneRowIsBad()
    {
        using var testDb = new SqliteTestDb();
        using var db = testDb.Open();
        await Section1.SeedBranchesAsync(db);

        var report = await Run(db, File(GoodBook, "Book,No Author,d,B901,CEN,Available,,Romance,,,,,,"), stopOnAnyError: true);

        Assert.Equal(0, report.ImportedCount);
        Assert.Equal(1, report.ReadyCount);
        Assert.NotNull(report.NotImportedReason);
        Assert.Equal(0, await db.Items.CountAsync());
    }

    [Fact]
    public async Task LibraryCodes_MustBeNewBothInTheFileAndInTheCatalogue_IgnoringCase()
    {
        using var testDb = new SqliteTestDb();
        using var db = testDb.Open();
        var (central, _, _) = await Section1.SeedBranchesAsync(db);
        await Section1.AddBookAsync(db, "B777", central);

        var report = await Run(db, File(
            "Book,One,d,B900,CEN,,A,G,,,,,,",
            "Book,Two,d,b900,CEN,,A,G,,,,,,",
            "Book,Three,d,b777,CEN,,A,G,,,,,,"));

        Assert.Equal(1, report.ImportedCount);
        Assert.Contains("already used on line 2", report.Rows[1].Messages.Single());
        Assert.Contains("already exists", report.Rows[2].Messages.Single());
    }

    [Fact]
    public async Task EachFieldProblem_GetsItsOwnMessage()
    {
        using var testDb = new SqliteTestDb();
        using var db = testDb.Open();
        var (_, north, _) = await Section1.SeedBranchesAsync(db);
        north.IsActive = false;
        await db.SaveChangesAsync();

        var report = await Run(db, File(
            "DVD,Doc,d,X1,CEN,,,,,,,,,",
            ",NoType,d,X2,CEN,,,,,,,,,",
            "Book,Lost,d,X3,ZZZ,,A,G,,,,,,",
            "Book,Closed,d,X4,NTH,,A,G,,,,,,",
            "Book,Borrowed,d,X5,CEN,Borrowed,A,G,,,,,,",
            "Book,BadYear,d,X6,CEN,,A,G,twenty,,,,,",
            "Book,OldYear,d,X7,CEN,,A,G,12,,,,,",
            "Toy,TooOld,d,X8,CEN,,,,,,,Puzzle,40,",
            "Music,NoArtist,d,X9,CEN,,,Rock,,,,,,",
            "Book,,d,,CEN,,A,G,,,,,,"));

        Assert.Equal(0, report.ImportedCount);
        string First(int row) => report.Rows[row].Messages.First();
        Assert.Contains("\"DVD\" is not valid", First(0));
        Assert.Contains("Type is required", First(1));
        Assert.Contains("\"ZZZ\" does not match any branch", First(2));
        Assert.Contains("not active", First(3));
        Assert.Contains("\"Borrowed\" is not allowed", First(4));
        Assert.Contains("PublicationYear must be a whole number", First(5));
        Assert.Contains("valid publication year", First(6));
        Assert.Contains("between 0 and 18", First(7));
        Assert.Contains("Artist is required", First(8));
        Assert.Contains("Item name is required.", report.Rows[9].Messages);
        Assert.Contains("Library code is required.", report.Rows[9].Messages);
    }

    [Fact]
    public async Task HeaderNamesIgnoreCaseSpacesAndOrder_UnknownColumnsAreWarnedAbout()
    {
        using var testDb = new SqliteTestDb();
        using var db = testDb.Open();
        var csv = "﻿genre,LIBRARY CODE,author,Type,Name,description,Colour\r\nRomance,B900,Jane Austen,book,Emma,A comedy.,red\r\n";

        var report = await Run(db, csv);

        Assert.True(report.FileAccepted, report.FileError);
        Assert.Equal(1, report.ImportedCount);
        Assert.Contains("Colour", report.Warnings.Single());
    }

    [Fact]
    public async Task ARowWithMoreValuesThanTheHeader_IsFlagged()
    {
        using var testDb = new SqliteTestDb();
        using var db = testDb.Open();

        var report = await Run(db, "Type,Name,Description,LibraryCode,Author,Genre\r\nBook,Emma,A comedy, with a comma,B900,Jane Austen,Romance\r\n");

        Assert.Equal(1, report.InvalidCount);
        Assert.Contains("7 values but the header has 6", report.Rows[0].Messages[0]);
    }

    [Fact]
    public async Task TheWholeFileIsRejected_WhenItCannotBeUsedAtAll()
    {
        using var testDb = new SqliteTestDb();
        using var db = testDb.Open();
        var service = new ItemImportService(db);
        Task<ImportReport> Upload(byte[] bytes) => service.ImportAsync(new MemoryStream(bytes), new ImportOptions());

        Assert.Contains("empty", (await Upload(Array.Empty<byte>())).FileError);
        Assert.Contains("no items", (await Upload(Encoding.UTF8.GetBytes(Header + "\r\n"))).FileError);
        Assert.Contains("Description", (await Upload(Encoding.UTF8.GetBytes("Type,Name,LibraryCode\r\nBook,A,B\r\n"))).FileError);
        Assert.Contains("more than once", (await Upload(Encoding.UTF8.GetBytes("Type,Name,Name,Description,LibraryCode\r\nBook,A,B,C,D\r\n"))).FileError);
        Assert.Contains("closing quote", (await Upload(Encoding.UTF8.GetBytes(Header + "\r\nBook,\"oops\r\n"))).FileError);
        Assert.Contains("not UTF-8", (await Upload(Encoding.Latin1.GetBytes("Type,Name,Description,LibraryCode\r\nBook,Café,d,C1\r\n"))).FileError);
        Assert.Equal(0, await db.Items.CountAsync());
    }

    [Fact]
    public async Task TooManyRowsOrTooBigAFile_IsRejected()
    {
        using var testDb = new SqliteTestDb();
        using var db = testDb.Open();
        var rows = Enumerable.Range(0, ItemImportService.MaxRows + 1).Select(i => $"Book,N{i},d,C{i},CEN,,A,G,,,,,,");
        Assert.Contains("limit is", (await Run(db, File(rows.ToArray()))).FileError);

        var huge = new byte[ItemImportService.MaxBytes + 1];
        var service = new ItemImportService(db);
        Assert.Contains("larger than", (await service.ImportAsync(new MemoryStream(huge), new ImportOptions())).FileError);
        Assert.Equal(0, await db.Items.CountAsync());
    }
}
