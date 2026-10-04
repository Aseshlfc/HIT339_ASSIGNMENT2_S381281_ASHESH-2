using System.ComponentModel.DataAnnotations;

namespace LibraryManagementSystem.Models;

public class Borrower
{
    public int Id { get; set; }

    [Required(ErrorMessage = "Borrower name is required.")]
    [StringLength(120, ErrorMessage = "Name cannot be more than 120 characters.")]
    [Display(Name = "Full Name")]
    public string FullName { get; set; } = string.Empty;

    [EmailAddress(ErrorMessage = "Please enter a valid email address.")]
    [StringLength(160, ErrorMessage = "Email cannot be more than 160 characters.")]
    public string Email { get; set; } = string.Empty;

    [Phone(ErrorMessage = "Please enter a valid phone number.")]
    [StringLength(30, ErrorMessage = "Phone number cannot be more than 30 characters.")]
    public string Phone { get; set; } = string.Empty;

    [StringLength(250, ErrorMessage = "Address cannot be more than 250 characters.")]
    public string Address { get; set; } = string.Empty;

    public ICollection<Loan> Loans { get; set; } = new List<Loan>();
}