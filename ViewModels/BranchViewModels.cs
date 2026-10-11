using LibraryManagementSystem.Models;

namespace LibraryManagementSystem.ViewModels;

public class BranchListItemViewModel
{
    public Branch Branch { get; set; } = null!;
    public int ItemCount { get; set; }
    public int AvailableCount { get; set; }
    public int StaffCount { get; set; }
    public int OpenTransferCount { get; set; }
}

public class BranchDetailsViewModel
{
    public Branch Branch { get; set; } = null!;
    public List<Item> Items { get; set; } = new();
    public List<BranchStaffRow> Staff { get; set; } = new();
    public List<ItemTransfer> OpenTransfers { get; set; } = new();
    public ItemStatus? StatusFilter { get; set; }
    public int TotalItems { get; set; }
}

public class BranchStaffRow
{
    public string UserName { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
}
