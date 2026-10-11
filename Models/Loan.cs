using System.ComponentModel.DataAnnotations;

namespace LibraryManagementSystem.Models;

public class Loan
{
    public int Id { get; set; }

    [Required]
    public int ItemId { get; set; }
    public Item Item { get; set; } = null!;

    [Required]
    public int BorrowerId { get; set; }
    public Borrower Borrower { get; set; } = null!;

    // Branch that issued the loan. Null for loans made before the library
    // became multi-branch, because the location was never recorded.
    public int? BranchId { get; set; }
    public Branch? Branch { get; set; }

    public DateTime BorrowedAt { get; set; }
    public DateTime DueDate { get; set; }
    public DateTime? ReturnedAt { get; set; }

    [Range(0, 999999)]
    public decimal FineAmount { get; set; }

    public bool FinePaid { get; set; }

    public bool IsReturned => ReturnedAt.HasValue;
}
