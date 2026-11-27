using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using UFSWebApp.Data;

namespace UFSWebApp.Controllers
{
    /// <summary>Notification centre for every signed-in role.</summary>
    [Authorize]
    public class NotificationsController : Controller
    {
        private readonly AppDbContext _db;
        public NotificationsController(AppDbContext db) => _db = db;
        private int CurrentUserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        public async Task<IActionResult> Index()
        {
            var notifications = await _db.notifications.Where(n => n.userId == CurrentUserId)
                .OrderByDescending(n => n.DateSent).Take(100).ToListAsync();
            ViewBag.UnreadIds = notifications.Where(n => !n.IsRead).Select(n => n.notificationID).ToHashSet();
            foreach (var n in notifications.Where(n => !n.IsRead)) { n.IsRead = true; n.IsToasted = true; }
            await _db.SaveChangesAsync();
            return View(notifications);
        }
    }
}
