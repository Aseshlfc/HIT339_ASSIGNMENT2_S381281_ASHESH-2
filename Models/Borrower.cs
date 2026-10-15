using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.AspNetCore.Mvc.ModelBinding;

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

    // Kiosk PIN. Set only by staff through KioskPinService, never bound from a form.
    // The PIN itself is never stored: only a salted PBKDF2 hash (ASP.NET Core Identity's hasher).

    /// <summary>Hash of the patron's 6-digit kiosk PIN; null until staff set one.</summary>
    [BindNever]
    public string? KioskPinHash { get; set; }

    /// <summary>When the PIN was last set. Kiosk sessions started before this are ended.</summary>
    [BindNever]
    public DateTime? KioskPinSetAtUtc { get; set; }

    /// <summary>Wrong PINs since the last success or lockout.</summary>
    [BindNever]
    public int KioskPinFailedAttempts { get; set; }

    /// <summary>The kiosk refuses this card until then.</summary>
    [BindNever]
    public DateTime? KioskPinLockedUntilUtc { get; set; }

    [NotMapped]
    public bool HasKioskPin => KioskPinHash != null;

    /// <summary>
    /// The patron's library card number, used at the self-service kiosk.
    /// It is derived from <see cref="Id"/> (for example P0007), so it needs no
    /// database column or migration and every existing borrower already has one.
    /// </summary>
    [NotMapped]
    [Display(Name = "Card number")]
    public string CardNumber => FormatCardNumber(Id);

    public const string CardPrefix = "P";

    public static string FormatCardNumber(int id) => $"{CardPrefix}{id:D4}";

    /// <summary>
    /// Reads a card number typed or scanned at the kiosk. Accepts "P0007",
    /// "p7" or just "7", ignoring spaces. Returns false for anything else.
    /// </summary>
    public static bool TryParseCardNumber(string? input, out int id)
    {
        id = 0;
        if (string.IsNullOrWhiteSpace(input)) return false;

        var text = input.Trim().ToUpperInvariant();
        if (text.StartsWith(CardPrefix, StringComparison.Ordinal)) text = text[CardPrefix.Length..];
        if (text.Length == 0 || text.Length > 9 || !text.All(char.IsAsciiDigit)) return false;

        return int.TryParse(text, out id) && id > 0;
    }
}