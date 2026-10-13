using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;

namespace LibraryManagementSystem.Api;

/// <summary>
/// Requires a valid X-Api-Key header. Keys are only accepted in the header (never
/// in the URL, where they would end up in logs and browser history) and are
/// compared in constant time so response timing does not reveal how much of a
/// guess was right.
/// </summary>
public class ApiKeyFilter : IAuthorizationFilter
{
    private readonly PublicApiOptions _options;

    public ApiKeyFilter(IOptions<PublicApiOptions> options) => _options = options.Value;

    public void OnAuthorization(AuthorizationFilterContext context)
    {
        var configured = _options.ApiKeys.Where(k => !string.IsNullOrWhiteSpace(k)).ToArray();
        if (configured.Length == 0)
        {
            context.Result = Problem(StatusCodes.Status503ServiceUnavailable,
                "The public API is switched off because no API keys are configured.");
            return;
        }

        var provided = context.HttpContext.Request.Headers[PublicApiOptions.HeaderName].ToString();
        if (string.IsNullOrEmpty(provided))
        {
            Reject(context, $"Send your API key in the {PublicApiOptions.HeaderName} header.");
            return;
        }

        if (!configured.Any(key => KeysMatch(key, provided)))
        {
            Reject(context, "The API key is not valid.");
        }
    }

    // Hashing first gives both sides the same length, which FixedTimeEquals needs.
    private static bool KeysMatch(string expected, string provided) =>
        CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(expected)),
            SHA256.HashData(Encoding.UTF8.GetBytes(provided)));

    private static void Reject(AuthorizationFilterContext context, string detail)
    {
        context.HttpContext.Response.Headers.WWWAuthenticate = "ApiKey";
        context.Result = Problem(StatusCodes.Status401Unauthorized, detail);
    }

    private static ObjectResult Problem(int status, string detail) =>
        new(new ProblemDetails { Status = status, Title = status == 401 ? "Unauthorized" : "Service unavailable", Detail = detail })
        {
            StatusCode = status,
            ContentTypes = { "application/problem+json" }
        };
}
