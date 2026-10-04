using LibraryManagementSystem.Models;

namespace LibraryManagementSystem.ViewModels;

public class ReturnViewModel
{
    public int LoanId { get; set; }
    public string? ItemName { get; set; }
    public string? LibraryCode { get; set; }
    public string? BorrowerName { get; set; }
    public DateTime DueDate { get; set; }
    public decimal EstimatedFine { get; set; }
    public ItemStatus ReturnStatus { get; set; } = ItemStatus.Available;
}
