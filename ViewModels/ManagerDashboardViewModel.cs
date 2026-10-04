namespace LibraryManagementSystem.ViewModels;

public class ManagerDashboardViewModel
{
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
}

public class PopularItemRow
{
    public string Name { get; set; } = string.Empty;
    public string LibraryCode { get; set; } = string.Empty;
    public int BorrowCount { get; set; }
}
