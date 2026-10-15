using LibraryManagementSystem.Data;
using LibraryManagementSystem.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace LibraryManagementSystem.Services;

public enum KioskSignInStatus { Success, MissingCard, MissingPin, InvalidCredentials, NoPinSet, LockedOut }

/// <param name="PinChecked">True if the PIN was compared with the stored hash. At most
/// five attempts per lockout period ever get that far, however many arrive at once.</param>
public record KioskSignInResult(KioskSignInStatus Status, Borrower? Patron = null, bool PinChecked = false);

/// <summary>
/// Kiosk PINs: staff set them, the kiosk checks them. A PIN is exactly six digits
/// (kept as text, so leading zeros count) and is stored only as a salted hash.
/// Five wrong PINs lock the card at the kiosk for 15 minutes.
/// </summary>
public interface IKioskPinService
{
    /// <summary>Checks a card number and PIN typed at the kiosk.</summary>
    Task<KioskSignInResult> SignInAsync(string? cardNumber, string? pin);

    /// <summary>
    /// Sets or replaces a patron's PIN, clears any lockout and ends the patron's kiosk
    /// sessions. False if the borrower does not exist; throws if the PIN is not six digits.
    /// </summary>
    Task<bool> SetPinAsync(int borrowerId, string pin);
}

public class KioskPinService : IKioskPinService
{
    public const int PinLength = 6;
    public const int MaxFailedAttempts = 5;
    public static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    private static readonly PasswordHasher<Borrower> Hasher = new();

    // Checked against when the card is unknown, so a wrong card takes as long as a wrong PIN.
    private static readonly Lazy<string> UnknownCardHash = new(() => Hasher.HashPassword(new Borrower(), "000000"));

    private readonly LibraryContext _db;
    private readonly IKioskSessionStore _sessions;
    private readonly TimeProvider _time;

    public KioskPinService(LibraryContext db, IKioskSessionStore sessions, TimeProvider time)
    {
        _db = db;
        _sessions = sessions;
        _time = time;
    }

    /// <summary>Exactly six ASCII digits. Other Unicode digits are refused.</summary>
    public static bool IsValidPin(string? pin) =>
        pin != null && pin.Length == PinLength && pin.All(char.IsAsciiDigit);

    public async Task<KioskSignInResult> SignInAsync(string? cardNumber, string? pin)
    {
        if (string.IsNullOrWhiteSpace(cardNumber)) return new(KioskSignInStatus.MissingCard);
        if (!IsValidPin(pin)) return new(KioskSignInStatus.MissingPin);

        Borrower? patron = null;
        if (Borrower.TryParseCardNumber(cardNumber, out var id))
            patron = await _db.Borrowers.AsNoTracking().SingleOrDefaultAsync(b => b.Id == id);

        if (patron == null)
        {
            Hasher.VerifyHashedPassword(new Borrower(), UnknownCardHash.Value, pin!);
            return new(KioskSignInStatus.InvalidCredentials);
        }

        if (patron.KioskPinHash == null) return new(KioskSignInStatus.NoPinSet, patron);

        // Reserve this attempt before checking the PIN, in one UPDATE that SQLite runs
        // one at a time. The UPDATE only succeeds while the card is not locked, and the
        // fifth reservation locks it straight away. So however many attempts arrive at
        // once, at most five PINs are ever checked per lockout period. A correct PIN
        // then clears the count and the lock.
        var now = _time.GetUtcNow().UtcDateTime;
        var lockUntil = now + LockoutDuration;
        var reserved = await _db.Borrowers
            .Where(b => b.Id == patron.Id && (b.KioskPinLockedUntilUtc == null || b.KioskPinLockedUntilUtc <= now))
            .ExecuteUpdateAsync(s => s
                // An expired lock starts a fresh count.
                .SetProperty(b => b.KioskPinFailedAttempts,
                    b => b.KioskPinLockedUntilUtc != null ? 1 : b.KioskPinFailedAttempts + 1)
                .SetProperty(b => b.KioskPinLockedUntilUtc,
                    b => (b.KioskPinLockedUntilUtc != null ? 1 : b.KioskPinFailedAttempts + 1) >= MaxFailedAttempts
                        ? lockUntil
                        : (DateTime?)null));
        if (reserved == 0) return new(KioskSignInStatus.LockedOut, patron);

        var check = Hasher.VerifyHashedPassword(patron, patron.KioskPinHash, pin!);
        if (check == PasswordVerificationResult.Failed)
        {
            var lockedNow = await _db.Borrowers.Where(b => b.Id == patron.Id)
                .Select(b => b.KioskPinLockedUntilUtc).SingleAsync() > now;
            return new(lockedNow ? KioskSignInStatus.LockedOut : KioskSignInStatus.InvalidCredentials, patron, PinChecked: true);
        }

        await _db.Borrowers.Where(b => b.Id == patron.Id).ExecuteUpdateAsync(s => s
            .SetProperty(b => b.KioskPinFailedAttempts, 0)
            .SetProperty(b => b.KioskPinLockedUntilUtc, (DateTime?)null));

        if (check == PasswordVerificationResult.SuccessRehashNeeded)
        {
            var rehashed = Hasher.HashPassword(patron, pin!);
            await _db.Borrowers.Where(b => b.Id == patron.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(b => b.KioskPinHash, rehashed));
        }

        return new(KioskSignInStatus.Success, patron, PinChecked: true);
    }

    public async Task<bool> SetPinAsync(int borrowerId, string pin)
    {
        if (!IsValidPin(pin)) throw new ArgumentException("A kiosk PIN must be exactly six digits.", nameof(pin));

        // One UPDATE straight to the database, like the lockout counter, so the result never
        // depends on a stale copy of the borrower held by this context.
        var hash = Hasher.HashPassword(new Borrower { Id = borrowerId }, pin);
        var setAt = _time.GetUtcNow().UtcDateTime;
        var updated = await _db.Borrowers.Where(b => b.Id == borrowerId).ExecuteUpdateAsync(s => s
            .SetProperty(b => b.KioskPinHash, hash)
            .SetProperty(b => b.KioskPinSetAtUtc, setAt)
            .SetProperty(b => b.KioskPinFailedAttempts, 0)
            .SetProperty(b => b.KioskPinLockedUntilUtc, (DateTime?)null));
        if (updated == 0) return false;

        _sessions.EndAllForPatron(borrowerId);
        return true;
    }
}
