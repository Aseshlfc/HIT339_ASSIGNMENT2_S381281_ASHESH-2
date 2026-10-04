using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;

namespace LibraryManagementSystem.Tests;

internal class NoOpTempDataProvider : ITempDataProvider
{
    public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
    public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
}

internal static class ControllerTestHelpers
{
    /// <summary>
    /// Attaches an HttpContext (with TempData, and optionally a signed-in user identity)
    /// to a controller instance for tests that need TempData or User.Identity to work.
    /// </summary>
    public static void AttachContext(Controller controller, string? currentUserId = null)
    {
        var httpContext = new DefaultHttpContext();
        if (currentUserId != null)
        {
            var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, currentUserId) };
            httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));
        }
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
        controller.TempData = new TempDataDictionary(httpContext, new NoOpTempDataProvider());
    }
}
