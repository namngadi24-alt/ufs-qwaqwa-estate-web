using Microsoft.EntityFrameworkCore;
using UFSWebApp.Data;
using UFSWebApp.Models;

namespace UFSWebApp.Infrastructure
{
    /// <summary>Creates notifications from real system events (shown as a pop-up toast and kept in the notification centre).</summary>
    public static class Notify
    {
        public static void To(AppDbContext db, int userId, string message, string level = "info")
        {
            db.notifications.Add(new Notification { userId = userId, Message = message, Level = level });
        }

        public static async Task ToStaffAsync(AppDbContext db, string message, string level = "info")
        {
            var staffIds = await db.users
                .Where(u => (u.role.RoleName == "Admin" || (u.role.RoleName == "FinancialOfficer" && u.IsAuthorised)))
                .Select(u => u.UserId)
                .ToListAsync();
            foreach (var id in staffIds) To(db, id, message, level);
        }

        public static async Task ToAdminsAsync(AppDbContext db, string message, string level = "info")
        {
            var ids = await db.users.Where(u => u.role.RoleName == "Admin").Select(u => u.UserId).ToListAsync();
            foreach (var id in ids) To(db, id, message, level);
        }
    }
}

namespace UFSWebApp.Infrastructure
{
    public static class CustomerNotifications
    {
        public static void AddSuspendedNotification(AppDbContext db, int userId, string reason) =>
            Notify.To(db, userId,
                $"Your account has been SUSPENDED. Reason: {reason} While suspended you cannot make payments, update your profile or apply for accommodation. " +
                "You can still log in and submit an appeal from 'Account status / Appeal'.", "danger");

        public static void AddReinstatedNotification(AppDbContext db, int userId, string? note) =>
            Notify.To(db, userId,
                "Your account has been reinstated and all functions are available again." + (string.IsNullOrWhiteSpace(note) ? "" : $" Note: {note}"), "success");
    }
}
