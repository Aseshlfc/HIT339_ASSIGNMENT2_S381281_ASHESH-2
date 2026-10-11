using System.ComponentModel.DataAnnotations;

namespace LibraryManagementSystem.ViewModels;

public class StaffFormViewModel
{
    public string? Id { get; set; }

    [Required(ErrorMessage = "Username is required.")]
    [StringLength(50, ErrorMessage = "Username cannot be more than 50 characters.")]
    public string UserName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Please choose a role.")]
    public string Role { get; set; } = "Reception";

    [Display(Name = "Branch")]
    public int? BranchId { get; set; }

    [StringLength(100, MinimumLength = 6, ErrorMessage = "Password must be at least 6 characters.")]
    [DataType(DataType.Password)]
    public string? Password { get; set; }
}
