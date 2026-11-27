using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using UFSWebApp.Data;
using UFSWebApp.Models;

namespace UFSWebApp.ViewComponents
{
    public class NotificationBadgeViewComponent : ViewComponent
    {
        private readonly AppDbContext _db;
        public NotificationBadgeViewComponent(AppDbContext db) => _db = db;

        public async Task<IViewComponentResult> InvokeAsync()
        {
            var count = 0;
            if (int.TryParse(UserClaimsPrincipal.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
                count = await _db.notifications.CountAsync(n => n.userId == userId && !n.IsRead);
            return View(count);
        }
    }

    /// <summary>Shows real, not-yet-shown notifications as pop-up toasts, then marks them as shown.</summary>
    public class ToastsViewComponent : ViewComponent
    {
        private readonly AppDbContext _db;
        public ToastsViewComponent(AppDbContext db) => _db = db;

        public async Task<IViewComponentResult> InvokeAsync()
        {
            var list = new List<Notification>();
            if (int.TryParse(UserClaimsPrincipal.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
            {
                list = await _db.notifications
                    .Where(n => n.userId == userId && !n.IsToasted && !n.IsRead)
                    .OrderByDescending(n => n.DateSent).Take(4).ToListAsync();
                if (list.Count > 0)
                {
                    foreach (var n in list) n.IsToasted = true;
                    await _db.SaveChangesAsync();
                }
            }
            return View(list);
        }
    }
}
