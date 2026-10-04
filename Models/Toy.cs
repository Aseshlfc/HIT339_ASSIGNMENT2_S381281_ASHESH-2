using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace LibraryManagementSystem.Models;

public class Toy : Item
{
    [Required(ErrorMessage = "Toy type is required.")]
    [StringLength(100, ErrorMessage = "Toy type cannot be more than 100 characters.")]
    public string ToyType { get; set; } = string.Empty;

    [Range(0, 18, ErrorMessage = "Minimum age must be between 0 and 18.")]
    public int MinimumAge { get; set; }

    [StringLength(100, ErrorMessage = "Manufacturer cannot be more than 100 characters.")]
    public string Manufacturer { get; set; } = string.Empty;

    [NotMapped]
    public override string ItemType => "Toy";
}