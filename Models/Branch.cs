using System.ComponentModel.DataAnnotations;

namespace LibraryManagementSystem.Models;

public class Branch
{
    /// <summary>
    /// Id of the branch created by the multi-branch migration. Every item that
    /// existed before the upgrade was assigned to it, and it is the fallback
    /// for any item saved without an explicit branch.
    /// </summary>
    public const int DefaultBranchId = 1;

    public int Id { get; set; }

    [Required(ErrorMessage = "Branch code is required.")]
    [StringLength(10, ErrorMessage = "Branch code cannot be more than 10 characters.")]
    [RegularExpression("^[A-Za-z0-9-]+$", ErrorMessage = "Branch code can only contain letters, numbers and hyphens.")]
    [Display(Name = "Branch code")]
    public string Code { get; set; } = string.Empty;

    [Required(ErrorMessage = "Branch name is required.")]
    [StringLength(100, ErrorMessage = "Branch name cannot be more than 100 characters.")]
    [Display(Name = "Branch name")]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "Address is required.")]
    [StringLength(250, ErrorMessage = "Address cannot be more than 250 characters.")]
    public string Address { get; set; } = string.Empty;

    [Phone(ErrorMessage = "Please enter a valid phone number.")]
    [StringLength(30, ErrorMessage = "Phone number cannot be more than 30 characters.")]
    public string? Phone { get; set; }

    [Display(Name = "Active")]
    public bool IsActive { get; set; } = true;

    public ICollection<Item> Items { get; set; } = new List<Item>();
    public ICollection<ApplicationUser> Staff { get; set; } = new List<ApplicationUser>();
}
