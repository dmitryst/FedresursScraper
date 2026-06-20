using System.Security.Claims;
using Lots.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;

namespace FedresursScraper.Controllers.Utils;

public static class AdminAccessHelper
{
    public static async Task EnsureAuthenticatedAsync(HttpContext httpContext)
    {
        if (httpContext.User.Identity?.IsAuthenticated == true)
            return;

        var result = await httpContext.AuthenticateAsync(JwtBearerDefaults.AuthenticationScheme);
        if (result.Succeeded && result.Principal != null)
        {
            httpContext.User = result.Principal;
        }
    }

    public static async Task<bool> IsAdminAsync(HttpContext httpContext, LotsDbContext dbContext)
    {
        await EnsureAuthenticatedAsync(httpContext);

        var userIdString = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? httpContext.User.FindFirstValue("sub");

        if (!Guid.TryParse(userIdString, out Guid userId))
            return false;

        var user = await dbContext.Users.FindAsync(userId);
        return user?.IsAdmin == true;
    }
}
