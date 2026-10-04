using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace LibraryManagementSystem.Models;

public class Book : Item
{
    [Required(ErrorMessage = "Author is required.")]
    [StringLength(120, ErrorMessage = "Author cannot be more than 120 characters.")]
    public string Author { get; set; } = string.Empty;

    [Required(ErrorMessage = "Book genre is required.")]
    [StringLength(80, ErrorMessage = "Genre cannot be more than 80 characters.")]
    public string Genre { get; set; } = string.Empty;

    [Range(1000, 2100, ErrorMessage = "Please enter a valid publication year.")]
    public int? PublicationYear { get; set; }

    [NotMapped]
    public override string ItemType => "Book";
}