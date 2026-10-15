namespace LibraryManagementSystem.Api;

/// <summary>
/// Settings for the self-service kiosk (section "Kiosk" in appsettings.json).
/// </summary>
public class KioskOptions
{
    public const string SectionName = "Kiosk";
    public const string PolicyName = "kiosk";

    /// <summary>Card look-ups and checkouts allowed per client address per minute.</summary>
    public int RequestsPerMinute { get; set; } = 30;

    /// <summary>A kiosk session ends on the server after this long without a request.</summary>
    public int SessionIdleMinutes { get; set; } = 3;

    /// <summary>A kiosk session ends on the server after this long, however busy.</summary>
    public int SessionMaxMinutes { get; set; } = 20;

    /// <summary>How long one welcome screen's sign-in form stays usable.</summary>
    public int SignInFormMinutes { get; set; } = 60;
}
