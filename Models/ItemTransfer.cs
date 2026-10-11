using System.ComponentModel.DataAnnotations;

namespace LibraryManagementSystem.Models;

public enum TransferStatus
{
    Requested,
    InTransit,
    Completed,
    Cancelled,
    Rejected
}

/// <summary>Why an item's branch changed.</summary>
public enum TransferKind
{
    // A physical move through the request / dispatch / receive workflow.
    Transfer,
    // An Admin corrected a wrongly recorded location. Nothing was shipped.
    Correction,
    // A borrower returned the item at a different branch's desk.
    CrossBranchReturn
}

public class ItemTransfer
{
    public int Id { get; set; }

    public TransferKind Kind { get; set; } = TransferKind.Transfer;

    public int ItemId { get; set; }
    public Item Item { get; set; } = null!;

    public int FromBranchId { get; set; }
    public Branch FromBranch { get; set; } = null!;

    public int ToBranchId { get; set; }
    public Branch ToBranch { get; set; } = null!;

    public TransferStatus Status { get; set; } = TransferStatus.Requested;

    [StringLength(300)]
    public string? Reason { get; set; }

    public DateTime RequestedAt { get; set; }
    [StringLength(256)]
    public string RequestedBy { get; set; } = string.Empty;

    public DateTime? DispatchedAt { get; set; }
    [StringLength(256)]
    public string? DispatchedBy { get; set; }

    /// <summary>When the transfer was completed, cancelled or rejected.</summary>
    public DateTime? ClosedAt { get; set; }
    [StringLength(256)]
    public string? ClosedBy { get; set; }
    [StringLength(300)]
    public string? ClosureNote { get; set; }

    public bool IsOpen => Status is TransferStatus.Requested or TransferStatus.InTransit;
}
