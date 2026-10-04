using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace LibraryManagementSystem.Models;

public class Music : Item
{
    [Required(ErrorMessage = "Artist is required.")]
    [StringLength(120, ErrorMessage = "Artist cannot be more than 120 characters.")]
    public string Artist { get; set; } = string.Empty;

    [Range(1000, 2100, ErrorMessage = "Please enter a valid release year.")]
    public int? ReleaseYear { get; set; }

    [StringLength(80, ErrorMessage = "Genre cannot be more than 80 characters.")]
    public string Genre { get; set; } = string.Empty;

    [NotMapped]
    public override string ItemType => "Music";
}