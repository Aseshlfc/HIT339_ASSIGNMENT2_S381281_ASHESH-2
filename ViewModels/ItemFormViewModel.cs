using System.ComponentModel.DataAnnotations;
using LibraryManagementSystem.Models;

namespace LibraryManagementSystem.ViewModels;

public class ItemFormViewModel
{
    public int? Id { get; set; }

    [Required(ErrorMessage = "Please select an item type.")]
    public string ItemType { get; set; } = "Book";

    [Required(ErrorMessage = "Item name is required.")]
    [StringLength(120, ErrorMessage = "Item name cannot be more than 120 characters.")]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "Description is required.")]
    [StringLength(500, ErrorMessage = "Description cannot be more than 500 characters.")]
    public string Description { get; set; } = string.Empty;

    [Required(ErrorMessage = "Library code is required.")]
    [StringLength(30, ErrorMessage = "Library code cannot be more than 30 characters.")]
    [Display(Name = "Library Code")]
    public string LibraryCode { get; set; } = string.Empty;

    public ItemStatus Status { get; set; } = ItemStatus.Available;

    [StringLength(120, ErrorMessage = "Author cannot be more than 120 characters.")]
    public string? Author { get; set; }

    [StringLength(80, ErrorMessage = "Genre cannot be more than 80 characters.")]
    public string? BookGenre { get; set; }

    [Range(1000, 2100, ErrorMessage = "Please enter a valid publication year.")]
    public int? PublicationYear { get; set; }

    [StringLength(120, ErrorMessage = "Artist cannot be more than 120 characters.")]
    public string? Artist { get; set; }

    [StringLength(80, ErrorMessage = "Genre cannot be more than 80 characters.")]
    public string? MusicGenre { get; set; }

    [Range(1000, 2100, ErrorMessage = "Please enter a valid release year.")]
    public int? ReleaseYear { get; set; }

    [StringLength(100, ErrorMessage = "Toy type cannot be more than 100 characters.")]
    public string? ToyType { get; set; }

    [Range(0, 18, ErrorMessage = "Minimum age must be between 0 and 18.")]
    public int? MinimumAge { get; set; }

    [StringLength(100, ErrorMessage = "Manufacturer cannot be more than 100 characters.")]
    public string? Manufacturer { get; set; }
}