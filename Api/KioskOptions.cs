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
}
