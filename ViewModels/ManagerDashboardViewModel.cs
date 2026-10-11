namespace LibraryManagementSystem.ViewModels;

public class ManagerDashboardViewModel
{
    public int TotalItems { get; set; }
    public int TotalLoans { get; set; }
    public int ActiveLoans { get; set; }
    public int ReturnedLoans { get; set; }
    public int OverdueLoans { get; set; }

    public Dictionary<string, int> StatusCounts { get; set; } = new();
    public Dictionary<string, int> TypeCounts { get; set; } = new();

    public decimal TotalFines { get; set; }
    public decimal PaidFines { get; set; }
    public decimal OutstandingFines { get; set; }

    public List<PopularItemRow> PopularItems { get; set; } = new();

    public int OpenTransfers { get; set; }
    // Loans made before issuing branch was recorded.
    public int LoansWithoutBranch { get; set; }
    public List<BranchDashboardRow> Branches { get; set; } = new();
}

public class BranchDashboardRow
{
    public int BranchId { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public int Items { get; set; }
    public int Available { get; set; }
    public int Borrowed { get; set; }
    public int InTransit { get; set; }
    public int OutOfService { get; set; }
    public int LoansIssued { get; set; }
    public int ActiveLoans { get; set; }
    public int OverdueLoans { get; set; }
}

public class PopularItemRow
{
    public string Name { get; set; } = string.Empty;
    public string LibraryCode { get; set; } = string.Empty;
    public int BorrowCount { get; set; }
}
