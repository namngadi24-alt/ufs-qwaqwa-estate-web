using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using UFSWebApp.Infrastructure;
using UFSWebApp.Data;
using UFSWebApp.Models;
using UFSWebApp.Models.ViewModels;

namespace UFSWebApp.Controllers
{
    [Authorize(Roles = "Admin,FinancialOfficer")]
    public class SuspensionsController : Controller
    {
        private readonly AppDbContext _db;
        public SuspensionsController(AppDbContext db) => _db = db;

        private string ReviewerText =>
            $"{User.FindFirstValue(ClaimTypes.Name)} ({(User.IsInRole("Admin") ? "Admin" : "Financial Officer")})";

        private IActionResult BackToCustomers() =>
            RedirectToAction("Index", "Customers");

        private async Task<(User? user, Account? account)> LoadCustomerAsync(int userId)
        {
            var customerRoleId = await _db.roles.Where(r => r.RoleName == "Customer").Select(r => r.RoleId).FirstOrDefaultAsync();
            var user = await _db.users.FirstOrDefaultAsync(u => u.UserId == userId && u.RoleId == customerRoleId);
            var account = user == null ? null : await _db.accounts.FirstOrDefaultAsync(a => a.userId == userId);
            return (user, account);
        }

        [HttpGet]
        public async Task<IActionResult> Suspend(int id)
        {
            var (user, account) = await LoadCustomerAsync(id);
            if (user == null || account == null) return NotFound();

            if (AccountStatuses.IsSuspended(account.AccountStatus))
            {
                TempData["Error"] = $"{user.UserName} {user.UserSurname}'s account is already suspended.";
                return BackToCustomers();
            }

            return View(new SuspendAccountViewModel
            {
                UserId = user.UserId,
                CustomerName = $"{user.UserName} {user.UserSurname}",
                CustomerEmail = user.UserEmail
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Suspend(SuspendAccountViewModel model)
        {
            var (user, account) = await LoadCustomerAsync(model.UserId);
            if (user == null || account == null) return NotFound();

            if (!ModelState.IsValid)
            {
                model.CustomerName = $"{user.UserName} {user.UserSurname}";
                model.CustomerEmail = user.UserEmail;
                return View(model);
            }

            account.AccountStatus = AccountStatuses.Suspended;
            account.SuspensionReason = model.Reason.Trim();
            account.DateSuspended = DateTime.UtcNow;

            CustomerNotifications.AddSuspendedNotification(_db, user.UserId, account.SuspensionReason);
            await _db.SaveChangesAsync();

            TempData["Success"] = $"{user.UserName} {user.UserSurname}'s account has been suspended and they have been notified.";
            return BackToCustomers();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Reactivate(int id)
        {
            var (user, account) = await LoadCustomerAsync(id);
            if (user == null || account == null) return NotFound();

            if (!AccountStatuses.IsSuspended(account.AccountStatus))
            {
                TempData["Error"] = "This account is not suspended.";
                return BackToCustomers();
            }

            Reinstate(account);

            foreach (var appeal in await _db.appeals.Where(a => a.userId == id && a.Status == AppealStatuses.Pending).ToListAsync())
            {
                appeal.Status = AppealStatuses.Approved;
                appeal.ResponseNote = "Account reinstated.";
                appeal.ReviewedBy = ReviewerText;
                appeal.DateReviewed = DateTime.UtcNow;
            }

            CustomerNotifications.AddReinstatedNotification(_db, user.UserId, null);
            await _db.SaveChangesAsync();

            TempData["Success"] = $"{user.UserName} {user.UserSurname}'s account has been reinstated and they have been notified.";
            return BackToCustomers();
        }

        private static void Reinstate(Account account)
        {
            account.AccountStatus = AccountStatuses.Active;
            account.SuspensionReason = null;
            account.DateSuspended = null;
        }

        public async Task<IActionResult> Appeals(string? status = "Pending")
        {
            var query = _db.appeals.Include(a => a.user).ThenInclude(u => u!.accounts).AsQueryable();
            if (!string.IsNullOrWhiteSpace(status) && status != "All")
                query = query.Where(a => a.Status == status);

            ViewBag.Status = status ?? "All";
            ViewBag.PendingCount = await _db.appeals.CountAsync(a => a.Status == AppealStatuses.Pending);
            return View(await query.OrderByDescending(a => a.DateSubmitted).ToListAsync());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DecideAppeal(int id, string decision, string? responseNote)
        {
            var appeal = await _db.appeals.FirstOrDefaultAsync(a => a.AppealId == id);
            if (appeal == null) return NotFound();

            if (appeal.Status != AppealStatuses.Pending)
            {
                TempData["Error"] = $"Appeal #{appeal.AppealId} has already been {appeal.Status.ToLowerInvariant()}.";
                return RedirectToAction(nameof(Appeals));
            }

            decision = decision?.Trim().ToLowerInvariant();
            if (decision != "approve" && decision != "reject")
            {
                TempData["Error"] = "Invalid appeal decision.";
                return RedirectToAction(nameof(Appeals));
            }

            if (decision == "reject" && string.IsNullOrWhiteSpace(responseNote))
            {
                TempData["Error"] = "Please give the customer a reason when rejecting an appeal.";
                return RedirectToAction(nameof(Appeals));
            }

            appeal.ResponseNote = responseNote?.Trim();
            appeal.ReviewedBy = ReviewerText;
            appeal.DateReviewed = DateTime.UtcNow;

            if (decision == "approve")
            {
                appeal.Status = AppealStatuses.Approved;
                var account = await _db.accounts.FirstOrDefaultAsync(a => a.userId == appeal.userId);
                if (account != null) Reinstate(account);

                _db.notifications.Add(new Notification
                {
                    userId = appeal.userId,
                    Message = "Your appeal was APPROVED."
                });
                CustomerNotifications.AddReinstatedNotification(_db, appeal.userId, appeal.ResponseNote);
            }
            else
            {
                appeal.Status = AppealStatuses.Rejected;
                _db.notifications.Add(new Notification
                {
                    userId = appeal.userId,
                    Message = $"Your appeal was not approved and your account remains suspended. Reason: {appeal.ResponseNote} You may submit a new appeal."
                });
            }

            await _db.SaveChangesAsync();
            TempData["Success"] = $"Appeal #{appeal.AppealId} {appeal.Status.ToLowerInvariant()}. The customer has been notified.";
            return RedirectToAction(nameof(Appeals));
        }
    }
}
