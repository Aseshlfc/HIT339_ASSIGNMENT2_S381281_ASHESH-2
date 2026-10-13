namespace LibraryManagementSystem.Api;

/// <summary>
/// Settings for the public read-only catalogue API (section "PublicApi" in
/// appsettings.json). Override them with environment variables in real use,
/// for example PublicApi__ApiKeys__0=your-secret-key.
/// </summary>
public class PublicApiOptions
{
    public const string SectionName = "PublicApi";
    public const string PolicyName = "public-api";
    public const string HeaderName = "X-Api-Key";

    /// <summary>Keys that may call the API. With none configured the API is switched off.</summary>
    public string[] ApiKeys { get; set; } = [];

    /// <summary>Requests allowed per client address per minute.</summary>
    public int RequestsPerMinute { get; set; } = 60;

    public int DefaultPageSize { get; set; } = 20;
    public int MaxPageSize { get; set; } = 100;
}
