using LibraryManagementSystem.Data;
using LibraryManagementSystem.Models;
using LibraryManagementSystem.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace LibraryManagementSystem.Services;

public interface IReportService
{
    Task<ManagerDashboardViewModel> BuildDashboardAsync();
    Task<ReportDocument> BuildBorrowingReportAsync(ReportFilter filter);
    Task<ReportDocument> BuildFineAuditReportAsync(ReportFilter filter);
    Task<ReportDocument> BuildInventoryHealthReportAsync(ReportFilter filter);
}

/// <summary>
/// Builds the manager reports. Rows are loaded first and aggregated in memory
/// because SQLite stores decimal values (Loan.FineAmount) as TEXT, so summing
/// or comparing them in SQL is either unsupported or compares strings.
/// </summary>
public class ReportService : IReportService
{
    public const string NoBranchLabel = "Not recorded";

    private readonly LibraryContext _db;
    public ReportService(LibraryContext db) => _db = db;

    public async Task<ManagerDashboardViewModel> BuildDashboardAsync()
    {
        var today = DateTime.Now.Date;
        var loans = await _db.Loans.AsNoTracking().ToListAsync();
        var items = await _db.Items.AsNoTracking().ToListAsync();
        var branches = await _db.Branches.AsNoTracking().OrderBy(b => b.Name).ToListAsync();

        var model = new ManagerDashboardViewModel
        {
            TotalItems = items.Count,
            TotalLoans = loans.Count,
            ActiveLoans = loans.Count(l => !l.ReturnedAt.HasValue),
            ReturnedLoans = loans.Count(l => l.ReturnedAt.HasValue),
            OverdueLoans = loans.Count(l => IsOverdue(l, today)),
            StatusCounts = Enum.GetValues<ItemStatus>()
                .ToDictionary(s => s.Label(), s => items.Count(i => i.Status == s)),
            TypeCounts = new Dictionary<string, int>
            {
                ["Books"] = items.OfType<Book>().Count(),
                ["Music"] = items.OfType<Music>().Count(),
                ["Toys"] = items.OfType<Toy>().Count()
            },
            TotalFines = loans.Sum(l => l.FineAmount),
            PaidFines = loans.Where(l => l.FinePaid).Sum(l => l.FineAmount),
            OutstandingFines = loans.Where(l => !l.FinePaid).Sum(l => l.FineAmount),
            OpenTransfers = await _db.ItemTransfers.CountAsync(t =>
                t.Status == TransferStatus.Requested || t.Status == TransferStatus.InTransit),
            LoansWithoutBranch = loans.Count(l => l.BranchId == null)
        };

        model.Branches = branches.Select(b =>
        {
            var held = items.Where(i => i.BranchId == b.Id).ToList();
            var issued = loans.Where(l => l.BranchId == b.Id).ToList();
            return new BranchDashboardRow
            {
                BranchId = b.Id,
                Name = b.Name,
                IsActive = b.IsActive,
                Items = held.Count,
                Available = held.Count(i => i.Status == ItemStatus.Available),
                Borrowed = held.Count(i => i.Status == ItemStatus.Borrowed),
                InTransit = held.Count(i => i.Status == ItemStatus.InTransit),
                OutOfService = held.Count(i => i.Status is ItemStatus.Damaged or ItemStatus.Destroy),
                LoansIssued = issued.Count,
                ActiveLoans = issued.Count(l => !l.ReturnedAt.HasValue),
                OverdueLoans = issued.Count(l => IsOverdue(l, today))
            };
        }).ToList();

        var itemsById = items.ToDictionary(i => i.Id);
        model.PopularItems = loans
            .Where(l => itemsById.ContainsKey(l.ItemId))
            .GroupBy(l => l.ItemId)
            .Select(g => new PopularItemRow
            {
                Name = itemsById[g.Key].Name,
                LibraryCode = itemsById[g.Key].LibraryCode,
                BorrowCount = g.Count()
            })
            .OrderByDescending(x => x.BorrowCount)
            .ThenBy(x => x.Name)
            .Take(10)
            .ToList();

        return model;
    }

    public async Task<ReportDocument> BuildBorrowingReportAsync(ReportFilter filter)
    {
        var today = DateTime.Now.Date;
        var branchNames = await BranchNamesAsync();
        var loans = Apply(await LoadLoansAsync(), filter, l => l.BorrowedAt);

        var report = new ReportDocument
        {
            Key = "borrowing",
            Title = "Borrowing Statistics Report",
            Description = "Borrowing activity by period, item category and issuing branch, with the most borrowed items and a full loan listing.",
            Filters = DescribeFilters(filter, branchNames, "Borrowed"),
            Summary =
            {
                new("Total loans", loans.Count),
                new("Active loans", loans.Count(l => !l.ReturnedAt.HasValue)),
                new("Returned loans", loans.Count(l => l.ReturnedAt.HasValue)),
                new("Overdue loans", loans.Count(l => IsOverdue(l, today)), Warn: loans.Any(l => IsOverdue(l, today))),
                new("Returned late", loans.Count(l => l.ReturnedAt.HasValue && l.ReturnedAt.Value.Date > l.DueDate.Date)),
                new("Borrowers served", loans.Select(l => l.BorrowerId).Distinct().Count()),
                new("Distinct items borrowed", loans.Select(l => l.ItemId).Distinct().Count())
            },
            Notes =
            {
                "Issuing branch is recorded for loans made after the multi-branch upgrade. Earlier loans are listed as \"" + NoBranchLabel + "\" because their location was never captured."
            }
        };

        report.Tables.Add(ActivityTable("Borrowing by item category", "Category",
            loans.GroupBy(l => l.Item.ItemType).OrderBy(g => g.Key), today));

        report.Tables.Add(ActivityTable("Borrowing by issuing branch", "Branch",
            loans.GroupBy(l => BranchLabel(l.BranchId, branchNames)).OrderBy(g => g.Key), today));

        report.Tables.Add(new ReportTable
        {
            Title = "Borrowing by month",
            Columns = { new("Month"), new("Loans", ReportValueKind.Number) },
            Rows = loans.GroupBy(l => l.BorrowedAt.ToString("yyyy-MM"))
                .OrderBy(g => g.Key)
                .Select(g => new object?[] { g.Key, g.Count() })
                .ToList()
        });

        report.Tables.Add(new ReportTable
        {
            Title = "Most borrowed items (top 10)",
            Columns = { new("Item"), new("Library code"), new("Category"), new("Times borrowed", ReportValueKind.Number) },
            Rows = loans.GroupBy(l => l.ItemId)
                .Select(g => new { g.First().Item, Count = g.Count() })
                .OrderByDescending(x => x.Count).ThenBy(x => x.Item.Name)
                .Take(10)
                .Select(x => new object?[] { x.Item.Name, x.Item.LibraryCode, x.Item.ItemType, x.Count })
                .ToList()
        });

        report.Tables.Add(new ReportTable
        {
            Title = "Loan details",
            Columns =
            {
                new("Loan #", ReportValueKind.Number), new("Borrowed", ReportValueKind.Date), new("Item"), new("Library code"),
                new("Category"), new("Borrower"), new("Issuing branch"), new("Due", ReportValueKind.Date),
                new("Returned", ReportValueKind.Date), new("Status")
            },
            Rows = loans.OrderByDescending(l => l.BorrowedAt)
                .Select(l => new object?[]
                {
                    l.Id, l.BorrowedAt, l.Item.Name, l.Item.LibraryCode, l.Item.ItemType, l.Borrower.FullName,
                    BranchLabel(l.BranchId, branchNames), l.DueDate, l.ReturnedAt,
                    l.ReturnedAt.HasValue ? "Returned" : IsOverdue(l, today) ? "Overdue" : "Active"
                })
                .ToList()
        });

        return report;
    }

    public async Task<ReportDocument> BuildFineAuditReportAsync(ReportFilter filter)
    {
        var today = DateTime.Now.Date;
        var branchNames = await BranchNamesAsync();
        var allLoans = await LoadLoansAsync();

        // A fine is assessed when the item is returned, so the date range is
        // applied to the return date.
        var fined = Apply(allLoans.Where(l => l.FineAmount > 0m), filter, l => l.ReturnedAt ?? l.DueDate);
        if (string.Equals(filter.FineStatus, "Paid", StringComparison.OrdinalIgnoreCase))
            fined = fined.Where(l => l.FinePaid).ToList();
        else if (string.Equals(filter.FineStatus, "Outstanding", StringComparison.OrdinalIgnoreCase))
            fined = fined.Where(l => !l.FinePaid).ToList();

        // Loans that are overdue today but not returned: no fine exists yet.
        var accruing = Apply(allLoans.Where(l => IsOverdue(l, today)), new ReportFilter { BranchId = filter.BranchId, ItemType = filter.ItemType }, l => l.BorrowedAt)
            .Select(l => new { Loan = l, Days = (today - l.DueDate.Date).Days })
            .ToList();

        var assessed = fined.Sum(l => l.FineAmount);
        var paid = fined.Where(l => l.FinePaid).Sum(l => l.FineAmount);
        var outstanding = fined.Where(l => !l.FinePaid).Sum(l => l.FineAmount);

        var filters = DescribeFilters(filter, branchNames, "Returned");
        filters.Add(new("Fine status", string.IsNullOrWhiteSpace(filter.FineStatus) ? "Paid and outstanding" : filter.FineStatus!));

        var report = new ReportDocument
        {
            Key = "fine-revenue-audit",
            Title = "Fine Revenue Audit",
            Description = $"Late fines assessed at {ReportFormat.Display(LibraryRules.FinePerLateDay, ReportValueKind.Money)} per overdue day, split into paid and outstanding amounts, with every fined loan listed for audit.",
            Filters = filters,
            Summary =
            {
                new("Fines assessed", assessed, ReportValueKind.Money),
                new("Marked as paid", paid, ReportValueKind.Money),
                new("Outstanding", outstanding, ReportValueKind.Money, Warn: outstanding > 0m),
                new("Fined loans", fined.Count),
                new("Loans paid", fined.Count(l => l.FinePaid)),
                new("Loans outstanding", fined.Count(l => !l.FinePaid)),
                new("Collection rate", assessed == 0m ? null : Math.Round(paid / assessed * 100m, 1), ReportValueKind.Percent),
                new("Average fine", fined.Count == 0 ? null : Math.Round(assessed / fined.Count, 2), ReportValueKind.Money),
                new("Accruing on overdue loans", accruing.Sum(a => a.Days * LibraryRules.FinePerLateDay), ReportValueKind.Money)
            },
            Notes =
            {
                "The date range applies to the return date, which is when a fine is assessed.",
                "Payment is stored as a paid/unpaid flag on the loan. The system does not record payment dates or individual payment transactions, so none are shown.",
                "\"Accruing on overdue loans\" is an estimate as of today for items not yet returned. It is not included in the assessed total and ignores the date range."
            }
        };

        report.Tables.Add(MoneyTable("Fines by issuing branch", "Branch",
            fined.GroupBy(l => BranchLabel(l.BranchId, branchNames)).OrderBy(g => g.Key)));

        report.Tables.Add(MoneyTable("Fines by borrower", "Borrower",
            fined.GroupBy(l => l.Borrower.FullName).OrderBy(g => g.Key)));

        report.Tables.Add(new ReportTable
        {
            Title = "Fined loans",
            Columns =
            {
                new("Loan #", ReportValueKind.Number), new("Item"), new("Library code"), new("Borrower"), new("Issuing branch"),
                new("Due", ReportValueKind.Date), new("Returned", ReportValueKind.Date), new("Days late", ReportValueKind.Number),
                new("Fine", ReportValueKind.Money), new("Status")
            },
            Rows = fined.OrderByDescending(l => l.ReturnedAt)
                .Select(l => new object?[]
                {
                    l.Id, l.Item.Name, l.Item.LibraryCode, l.Borrower.FullName, BranchLabel(l.BranchId, branchNames),
                    l.DueDate, l.ReturnedAt,
                    l.ReturnedAt.HasValue ? Math.Max(0, (l.ReturnedAt.Value.Date - l.DueDate.Date).Days) : null,
                    l.FineAmount, l.FinePaid ? "Paid" : "Outstanding"
                })
                .ToList(),
            EmptyMessage = "No fined loans match the selected filters."
        });

        report.Tables.Add(new ReportTable
        {
            Title = "Overdue loans not yet returned (fine not yet assessed)",
            Columns =
            {
                new("Loan #", ReportValueKind.Number), new("Item"), new("Library code"), new("Borrower"), new("Issuing branch"),
                new("Due", ReportValueKind.Date), new("Days overdue", ReportValueKind.Number), new("Estimated fine", ReportValueKind.Money)
            },
            Rows = accruing.OrderByDescending(a => a.Days)
                .Select(a => new object?[]
                {
                    a.Loan.Id, a.Loan.Item.Name, a.Loan.Item.LibraryCode, a.Loan.Borrower.FullName,
                    BranchLabel(a.Loan.BranchId, branchNames), a.Loan.DueDate, a.Days, a.Days * LibraryRules.FinePerLateDay
                })
                .ToList(),
            EmptyMessage = "No loans are currently overdue."
        });

        return report;
    }

    public async Task<ReportDocument> BuildInventoryHealthReportAsync(ReportFilter filter)
    {
        var today = DateTime.Now.Date;
        var branches = await _db.Branches.AsNoTracking().OrderBy(b => b.Name).ToListAsync();
        var branchNames = branches.ToDictionary(b => b.Id, b => b.Name);

        var items = (await _db.Items.AsNoTracking().ToListAsync())
            .Where(i => filter.BranchId == null || i.BranchId == filter.BranchId)
            .Where(i => string.IsNullOrWhiteSpace(filter.ItemType) || i.ItemType == filter.ItemType)
            .OrderBy(i => i.LibraryCode)
            .ToList();

        var openLoans = (await _db.Loans.AsNoTracking().Where(l => l.ReturnedAt == null).ToListAsync())
            .GroupBy(l => l.ItemId).ToDictionary(g => g.Key, g => g.First());
        var openTransfers = (await _db.ItemTransfers.AsNoTracking()
                .Where(t => t.Status == TransferStatus.Requested || t.Status == TransferStatus.InTransit)
                .ToListAsync())
            .GroupBy(t => t.ItemId).ToDictionary(g => g.Key, g => g.First());

        int Count(ItemStatus s) => items.Count(i => i.Status == s);
        var outOfService = Count(ItemStatus.Damaged) + Count(ItemStatus.Destroy);

        var filters = DescribeFilters(filter, branchNames, dateLabel: null);

        var report = new ReportDocument
        {
            Key = "inventory-health",
            Title = "Inventory Health Report",
            Description = "Current stock by status, branch and category, with the items that need attention.",
            Filters = filters,
            Summary =
            {
                new("Total items", items.Count),
                new("Available", Count(ItemStatus.Available)),
                new("Borrowed", Count(ItemStatus.Borrowed)),
                new("In transit", Count(ItemStatus.InTransit)),
                new("Damaged", Count(ItemStatus.Damaged), Warn: Count(ItemStatus.Damaged) > 0),
                new("Destroy", Count(ItemStatus.Destroy), Warn: Count(ItemStatus.Destroy) > 0),
                new("Availability rate", Rate(Count(ItemStatus.Available), items.Count), ReportValueKind.Percent),
                new("Out of service", Rate(outOfService, items.Count), ReportValueKind.Percent, Warn: outOfService > 0)
            },
            Notes =
            {
                "This report shows the inventory as it is now, so no date range applies.",
                "An item that is in transit is counted at its source branch until the destination confirms receipt."
            }
        };

        // Every branch is listed (even with no stock) unless one branch was selected.
        var branchRows = branches
            .Where(b => filter.BranchId == null || b.Id == filter.BranchId)
            .Select(b => StatusRow(b.Name + (b.IsActive ? "" : " (inactive)"), items.Where(i => i.BranchId == b.Id).ToList()))
            .ToList();
        report.Tables.Add(StatusTable("Inventory by branch", "Branch", branchRows));

        var categoryRows = items.GroupBy(i => i.ItemType).OrderBy(g => g.Key)
            .Select(g => StatusRow(g.Key, g.ToList())).ToList();
        report.Tables.Add(StatusTable("Inventory by category", "Category", categoryRows));

        var attention = new List<object?[]>();
        foreach (var item in items)
        {
            openLoans.TryGetValue(item.Id, out var loan);
            string? issue = item.Status switch
            {
                ItemStatus.Damaged => "Damaged: needs repair or replacement",
                ItemStatus.Destroy => "Marked for disposal",
                ItemStatus.InTransit => openTransfers.TryGetValue(item.Id, out var t)
                    ? $"In transit to {BranchLabel(t.ToBranchId, branchNames)}"
                    : "Marked In transit but has no open transfer",
                ItemStatus.Borrowed when loan == null => "Marked Borrowed but has no open loan",
                ItemStatus.Borrowed when loan!.DueDate.Date < today => $"On loan and {(today - loan.DueDate.Date).Days} day(s) overdue",
                ItemStatus.Available when loan != null => "Marked Available but still has an open loan",
                _ => null
            };
            if (issue != null)
                attention.Add(new object?[] { item.LibraryCode, item.Name, item.ItemType, BranchLabel(item.BranchId, branchNames), item.Status.Label(), issue });
        }
        report.Tables.Add(new ReportTable
        {
            Title = "Items needing attention",
            Description = "Damaged or disposed stock, items in transit, overdue loans, and any item whose status disagrees with its loan record.",
            Columns = { new("Library code"), new("Item"), new("Category"), new("Branch"), new("Status"), new("Issue") },
            Rows = attention,
            EmptyMessage = "No items need attention."
        });

        report.Tables.Add(new ReportTable
        {
            Title = "Inventory listing",
            Columns = { new("Library code"), new("Item"), new("Category"), new("Branch"), new("Status") },
            Rows = items.Select(i => new object?[] { i.LibraryCode, i.Name, i.ItemType, BranchLabel(i.BranchId, branchNames), i.Status.Label() }).ToList(),
            EmptyMessage = "No items match the selected filters."
        });

        return report;
    }

    // ---------- helpers ----------

    private static bool IsOverdue(Loan loan, DateTime today)
        => !loan.ReturnedAt.HasValue && loan.DueDate.Date < today;

    private static decimal? Rate(int part, int total)
        => total == 0 ? null : Math.Round(part * 100m / total, 1);

    private static string BranchLabel(int? branchId, IReadOnlyDictionary<int, string> names)
        => branchId == null ? NoBranchLabel : names.TryGetValue(branchId.Value, out var name) ? name : $"Branch #{branchId}";

    private async Task<Dictionary<int, string>> BranchNamesAsync()
        => await _db.Branches.AsNoTracking().ToDictionaryAsync(b => b.Id, b => b.Name);

    private Task<List<Loan>> LoadLoansAsync()
        => _db.Loans.AsNoTracking().Include(l => l.Item).Include(l => l.Borrower).ToListAsync();

    private static List<Loan> Apply(IEnumerable<Loan> loans, ReportFilter filter, Func<Loan, DateTime> date)
    {
        if (filter.From.HasValue) loans = loans.Where(l => date(l).Date >= filter.From.Value.Date);
        if (filter.To.HasValue) loans = loans.Where(l => date(l).Date <= filter.To.Value.Date);
        if (filter.BranchId.HasValue) loans = loans.Where(l => l.BranchId == filter.BranchId);
        if (!string.IsNullOrWhiteSpace(filter.ItemType)) loans = loans.Where(l => l.Item.ItemType == filter.ItemType);
        return loans.ToList();
    }

    private static List<KeyValuePair<string, string>> DescribeFilters(ReportFilter filter, IReadOnlyDictionary<int, string> branchNames, string? dateLabel)
    {
        var list = new List<KeyValuePair<string, string>>();
        if (dateLabel != null)
        {
            var range = (filter.From, filter.To) switch
            {
                (null, null) => "All dates",
                ({ } from, null) => $"From {from:dd MMM yyyy}",
                (null, { } to) => $"Up to {to:dd MMM yyyy}",
                ({ } from, { } to) => $"{from:dd MMM yyyy} to {to:dd MMM yyyy}"
            };
            list.Add(new($"{dateLabel} date range", range));
        }
        list.Add(new("Branch", filter.BranchId == null ? "All branches" : BranchLabel(filter.BranchId, branchNames)));
        list.Add(new("Item category", string.IsNullOrWhiteSpace(filter.ItemType) ? "All categories" : filter.ItemType!));
        return list;
    }

    private static ReportTable ActivityTable(string title, string firstColumn, IEnumerable<IGrouping<string, Loan>> groups, DateTime today)
        => new()
        {
            Title = title,
            Columns =
            {
                new(firstColumn), new("Loans", ReportValueKind.Number), new("Active", ReportValueKind.Number),
                new("Returned", ReportValueKind.Number), new("Overdue", ReportValueKind.Number)
            },
            Rows = groups.Select(g => new object?[]
            {
                g.Key, g.Count(), g.Count(l => !l.ReturnedAt.HasValue), g.Count(l => l.ReturnedAt.HasValue), g.Count(l => IsOverdue(l, today))
            }).ToList()
        };

    private static ReportTable MoneyTable(string title, string firstColumn, IEnumerable<IGrouping<string, Loan>> groups)
        => new()
        {
            Title = title,
            Columns =
            {
                new(firstColumn), new("Fined loans", ReportValueKind.Number), new("Assessed", ReportValueKind.Money),
                new("Paid", ReportValueKind.Money), new("Outstanding", ReportValueKind.Money)
            },
            Rows = groups.Select(g => new object?[]
            {
                g.Key, g.Count(), g.Sum(l => l.FineAmount),
                g.Where(l => l.FinePaid).Sum(l => l.FineAmount), g.Where(l => !l.FinePaid).Sum(l => l.FineAmount)
            }).ToList(),
            EmptyMessage = "No fined loans match the selected filters."
        };

    private static object?[] StatusRow(string label, List<Item> items)
        => new object?[]
        {
            label, items.Count,
            items.Count(i => i.Status == ItemStatus.Available), items.Count(i => i.Status == ItemStatus.Borrowed),
            items.Count(i => i.Status == ItemStatus.InTransit), items.Count(i => i.Status == ItemStatus.Damaged),
            items.Count(i => i.Status == ItemStatus.Destroy),
            Rate(items.Count(i => i.Status == ItemStatus.Available), items.Count)
        };

    private static ReportTable StatusTable(string title, string firstColumn, List<object?[]> rows)
        => new()
        {
            Title = title,
            Columns =
            {
                new(firstColumn), new("Total", ReportValueKind.Number), new("Available", ReportValueKind.Number),
                new("Borrowed", ReportValueKind.Number), new("In transit", ReportValueKind.Number),
                new("Damaged", ReportValueKind.Number), new("Destroy", ReportValueKind.Number),
                new("Availability", ReportValueKind.Percent)
            },
            Rows = rows,
            EmptyMessage = "No items match the selected filters."
        };
}
