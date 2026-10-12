using System.ComponentModel.DataAnnotations;

namespace LibraryManagementSystem.ViewModels;

/// <summary>
/// Form data for placing a hold on an item.
/// </summary>
public class ReserveViewModel
{
    [Required(ErrorMessage = "Library code is required.")]
    [Display(Name = "Library Code")]
    public string LibraryCode { get; set; } = string.Empty;

    [Range(1, int.MaxValue, ErrorMessage = "Please select a borrower.")]
    [Display(Name = "Borrower")]
    public int BorrowerId { get; set; }
}