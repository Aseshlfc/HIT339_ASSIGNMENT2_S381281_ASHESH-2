using LibraryManagementSystem.Models;
using Microsoft.EntityFrameworkCore;

namespace LibraryManagementSystem.Data;

public static class ItemLookupExtensions
{
    /// <summary>
    /// Finds an item by the library code a person typed or scanned: surrounding
    /// spaces are ignored and "b001" finds B001. An exact match wins; two items
    /// that differ only by case are ambiguous and are treated as not found.
    /// The reception desk, the hold form and the kiosk all look items up this way.
    /// </summary>
    public static async Task<Item?> FindItemByCodeAsync(this LibraryContext db, string? libraryCode)
    {
        var code = libraryCode?.Trim();
        if (string.IsNullOrEmpty(code)) return null;

        var upper = code.ToUpperInvariant();
        var matches = await db.Items.Where(i => i.LibraryCode.ToUpper() == upper).OrderBy(i => i.Id).Take(2).ToListAsync();
        return matches.FirstOrDefault(i => i.LibraryCode == code) ?? (matches.Count == 1 ? matches[0] : null);
    }
}
