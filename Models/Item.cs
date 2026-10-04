using System.ComponentModel.DataAnnotations;

namespace LibraryManagementSystem.Models;

public enum ItemStatus
{
    Available,
    Borrowed,
    Damaged,
    Destroy
}

public abstract class Item
{
    public int Id { get; set; }

    [Required(ErrorMessage = "Item name is required.")]
    [StringLength(120, ErrorMessage = "Item name cannot be more than 120 characters.")]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "Description is required.")]
    [StringLength(500, ErrorMessage = "Description cannot be more than 500 characters.")]
    public string Description { get; set; } = string.Empty;

    [Required(ErrorMessage = "Library code is required.")]
    [StringLength(30, ErrorMessage = "Library code cannot be more than 30 characters.")]
    public string LibraryCode { get; set; } = string.Empty;

    [Required]
    public ItemStatus Status { get; set; } = ItemStatus.Available;

    public ICollection<Loan> Loans { get; set; } = new List<Loan>();

    public abstract string ItemType { get; }
}