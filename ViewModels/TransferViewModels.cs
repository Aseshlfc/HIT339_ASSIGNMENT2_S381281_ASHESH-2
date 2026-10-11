using System.ComponentModel.DataAnnotations;
using LibraryManagementSystem.Models;

namespace LibraryManagementSystem.ViewModels;

public class TransferCreateViewModel
{
    [Required(ErrorMessage = "Please choose an item to transfer.")]
    [Display(Name = "Item")]
    public int? ItemId { get; set; }

    [Required(ErrorMessage = "Please choose a destination branch.")]
    [Display(Name = "Destination branch")]
    public int? ToBranchId { get; set; }

    [StringLength(300, ErrorMessage = "Reason cannot be more than 300 characters.")]
    [Display(Name = "Reason / notes")]
    public string? Reason { get; set; }
}

public class TransferRowViewModel
{
    public ItemTransfer Transfer { get; set; } = null!;
    public bool CanDispatch { get; set; }
    public bool CanReject { get; set; }
    public bool CanReceive { get; set; }
    public bool CanCancel { get; set; }
    public bool HasActions => CanDispatch || CanReject || CanReceive || CanCancel;
}

public class TransferIndexViewModel
{
    public List<TransferRowViewModel> Rows { get; set; } = new();
    public string View { get; set; } = "open";
    public int? BranchId { get; set; }
    public Branch? StaffBranch { get; set; }
    public bool IsAdmin { get; set; }
    public bool CanCreate { get; set; }
}

public class ItemDetailsViewModel
{
    public Item Item { get; set; } = null!;
    public string BranchName { get; set; } = string.Empty;
    public ItemTransfer? OpenTransfer { get; set; }
    public List<ItemTransfer> TransferHistory { get; set; } = new();
    public int LoanCount { get; set; }
    public Loan? ActiveLoan { get; set; }
}

public class ItemBranchCorrectionViewModel
{
    public int ItemId { get; set; }
    public string? ItemName { get; set; }
    public string? LibraryCode { get; set; }
    public string? CurrentBranchName { get; set; }

    [Required(ErrorMessage = "Please choose the branch that actually holds the item.")]
    [Display(Name = "Correct branch")]
    public int? ToBranchId { get; set; }

    [Required(ErrorMessage = "Please explain why the recorded location is wrong.")]
    [StringLength(300, ErrorMessage = "Reason cannot be more than 300 characters.")]
    public string? Reason { get; set; }
}

public class ReportPageViewModel
{
    public string ReportKey { get; set; } = string.Empty;
    public Services.ReportDocument Document { get; set; } = null!;
    public Services.ReportFilter Filter { get; set; } = new();
    public List<Branch> Branches { get; set; } = new();
    public bool ShowDateRange { get; set; }
    public bool ShowFineStatus { get; set; }
    public string DateLabel { get; set; } = "Borrowed";
}
