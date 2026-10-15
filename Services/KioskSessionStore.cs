using System.Collections.Concurrent;
using System.Security.Cryptography;
using LibraryManagementSystem.Api;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;

namespace LibraryManagementSystem.Services;

/// <summary>A message from the last checkout, shown once on the next account page.</summary>
public record KioskFlash(string Message, bool IsError, string? LibraryCode);

/// <summary>
/// One patron signed in at one kiosk. Only the random <see cref="Id"/> leaves the
/// server (in the kiosk cookie); the patron and branch are never taken from the browser.
/// </summary>
public sealed class KioskSession
{
    internal KioskSession(string id, int patronId, int branchId, DateTimeOffset now)
    {
        Id = id;
        PatronId = patronId;
        BranchId = branchId;
        StartedAt = now;
        LastSeenAt = now;
    }

    public string Id { get; }
    public int PatronId { get; }
    public int BranchId { get; }
    public DateTimeOffset StartedAt { get; }
    public DateTimeOffset LastSeenAt { get; internal set; }

    public KioskFlash? Flash { get; set; }

    public KioskFlash? TakeFlash()
    {
        var flash = Flash;
        Flash = null;
        return flash;
    }
}

/// <summary>
/// Short-lived kiosk sessions and one-time sign-in forms, kept in memory.
/// A restart signs everyone out, which is the safe outcome for a public kiosk.
/// </summary>
public interface IKioskSessionStore
{
    /// <summary>A tamper-proof token for one welcome screen, valid for one sign-in attempt.</summary>
    string CreateSignInToken(int branchId);

    /// <summary>
    /// False if the token is missing, altered, for another branch, expired or already used.
    /// Every call uses the token up, so a replayed sign-in form never works twice.
    /// </summary>
    bool RedeemSignInToken(string? token, int branchId);

    KioskSession Start(int patronId, int branchId);

    /// <summary>The live session with this id, or null if it never existed, ended or expired.</summary>
    KioskSession? Find(string? sessionId);

    void End(string? sessionId);
}

public class KioskSessionStore : IKioskSessionStore
{
    private readonly ConcurrentDictionary<string, KioskSession> _sessions = new();
    private readonly ConcurrentDictionary<string, DateTimeOffset> _usedTokens = new();
    private readonly IDataProtector _protector;
    private readonly KioskOptions _options;
    private readonly TimeProvider _time;

    public KioskSessionStore(IDataProtectionProvider dataProtection, IOptions<KioskOptions> options, TimeProvider time)
    {
        // The used-token list lives in memory, so tokens are also tied to this run of the
        // application: after a restart, a form used before it cannot be replayed.
        _protector = dataProtection.CreateProtector("LibraryManagementSystem.Kiosk.SignIn.v1", NewId(16));
        _options = options.Value;
        _time = time;
    }

    public string CreateSignInToken(int branchId)
    {
        var expires = _time.GetUtcNow().AddMinutes(_options.SignInFormMinutes);
        return _protector.Protect($"{NewId(16)}|{branchId}|{expires.ToUnixTimeSeconds()}");
    }

    public bool RedeemSignInToken(string? token, int branchId)
    {
        if (string.IsNullOrWhiteSpace(token)) return false;

        string payload;
        try { payload = _protector.Unprotect(token); }
        catch (CryptographicException) { return false; }

        var parts = payload.Split('|');
        if (parts.Length != 3
            || !int.TryParse(parts[1], out var tokenBranchId) || tokenBranchId != branchId
            || !long.TryParse(parts[2], out var expiresUnix))
            return false;

        var now = _time.GetUtcNow();
        var expires = DateTimeOffset.FromUnixTimeSeconds(expiresUnix);
        if (now >= expires) return false;

        foreach (var used in _usedTokens.Where(t => t.Value <= now))
            _usedTokens.TryRemove(used.Key, out _);

        return _usedTokens.TryAdd(parts[0], expires);
    }

    public KioskSession Start(int patronId, int branchId)
    {
        var now = _time.GetUtcNow();
        foreach (var stale in _sessions.Values.Where(s => IsExpired(s, now)))
            _sessions.TryRemove(stale.Id, out _);

        var session = new KioskSession(NewId(32), patronId, branchId, now);
        _sessions[session.Id] = session;
        return session;
    }

    public KioskSession? Find(string? sessionId)
    {
        if (string.IsNullOrEmpty(sessionId) || !_sessions.TryGetValue(sessionId, out var session))
            return null;

        var now = _time.GetUtcNow();
        if (IsExpired(session, now))
        {
            _sessions.TryRemove(sessionId, out _);
            return null;
        }

        session.LastSeenAt = now;
        return session;
    }

    public void End(string? sessionId)
    {
        if (!string.IsNullOrEmpty(sessionId))
            _sessions.TryRemove(sessionId, out _);
    }

    private bool IsExpired(KioskSession session, DateTimeOffset now) =>
        now - session.LastSeenAt >= TimeSpan.FromMinutes(_options.SessionIdleMinutes)
        || now - session.StartedAt >= TimeSpan.FromMinutes(_options.SessionMaxMinutes);

    private static string NewId(int bytes) =>
        Convert.ToHexString(RandomNumberGenerator.GetBytes(bytes)).ToLowerInvariant();
}
