using System.ComponentModel.DataAnnotations;

namespace LibraryManagementSystem.ViewModels;

public class BorrowViewModel
{
    [Required]
    [Display(Name = "Library code")]
    public string LibraryCode { get; set; } = string.Empty;

    [Required]
    [Display(Name = "Borrower")]
    public int BorrowerId { get; set; }
}
