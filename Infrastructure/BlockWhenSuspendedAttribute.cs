using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using UFSWebApp.Data;
using UFSWebApp.Models;

namespace UFSWebApp.Infrastructure
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
    public class BlockWhenSuspendedAttribute : Attribute, IAsyncActionFilter
    {
        public const string BlockedMessage =
            "Your account is suspended, so this function is unavailable until your account is reinstated. " +
            "You can submit an appeal below.";

        public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            var principal = context.HttpContext.User;

            if (principal.IsInRole("Customer") &&
                int.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
            {
                var db = context.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
                var status = await db.accounts
                    .Where(a => a.userId == userId)
                    .Select(a => a.AccountStatus)
                    .FirstOrDefaultAsync();

                if (AccountStatuses.IsSuspended(status))
                {
                    if (context.Controller is Controller controller)
                        controller.TempData["Error"] = BlockedMessage;

                    context.Result = new RedirectToActionResult("Appeal", "Customer", null);
                    return;
                }
            }

            await next();
        }
    }
}
