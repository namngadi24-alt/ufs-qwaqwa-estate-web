using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using UFSWebApp.Data;

namespace UFSWebApp.Infrastructure
{
    public class OfficerAuthorisationFilter : IAsyncActionFilter
    {
        private readonly AppDbContext _db;

        public OfficerAuthorisationFilter(AppDbContext db) => _db = db;

        public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            var principal = context.HttpContext.User;

            if (principal.IsInRole("FinancialOfficer") &&
                int.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
            {
                var isAuthorised = await _db.users
                    .Where(u => u.UserId == userId)
                    .Select(u => u.IsAuthorised)
                    .FirstOrDefaultAsync();

                if (!isAuthorised && context.HttpContext.Request.Path.StartsWithSegments("/api"))
                {
                    context.Result = new UnauthorizedObjectResult(new { message = "Your financial officer access has been revoked." });
                    return;
                }

                if (!isAuthorised)
                {
                    await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                    if (context.Controller is Controller controller)
                        controller.TempData["Error"] = "Your financial officer access has been revoked by the administrator.";
                    context.Result = new RedirectToActionResult("Login", "Account", null);
                    return;
                }
            }

            await next();
        }
    }
}
