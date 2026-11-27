using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using UFSWebApp.Data;
using UFSWebApp.Infrastructure;
using UFSWebApp.Models;
using UFSWebApp.Models.ViewModels;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace UFSWebApp.Controllers
{
    public class CustomerDashboardViewModel
    {
        public User Customer { get; set; }
        public Account Account { get; set; }
        public List<RoomAllocation> Allocations { get; set; } = new();
        public decimal Outstanding { get; set; }
        public decimal PendingAmount { get; set; }
        public List<Charge> OpenCharges { get; set; } = new();
        public List<Payment> RecentPayments { get; set; } = new();
        public List<AccommodationApplication> Applications { get; set; } = new();
        public int OpenMaintenance { get; set; }
        public bool IsSuspended { get; set; }
        public bool HasPendingAppeal { get; set; }
    }

    [Authorize(Roles = "Customer")]
    public class CustomerController : Controller
    {
        private readonly AppDbContext _db;
        private readonly BillingService _billing;
        private readonly PasswordHasher<User> _hasher = new();

        public CustomerController(AppDbContext db, BillingService billing) { _db = db; _billing = billing; }
        private int CurrentUserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        public async Task<IActionResult> Index()
        {
            var user = await _db.users.FirstOrDefaultAsync(u => u.UserId == CurrentUserId);
            var account = await _db.accounts.FirstOrDefaultAsync(a => a.userId == CurrentUserId);
            if (user == null || account == null) return NotFound();

            await _billing.EnsureChargesAsync(CurrentUserId);
            await _billing.SendRemindersAsync(CurrentUserId);

            var today = DateTime.Today;
            var pendingIds = await _billing.PendingChargeIdsAsync(CurrentUserId);
            var model = new CustomerDashboardViewModel
            {
                Customer = user,
                Account = account,
                Allocations = await _db.roomAllocations.AsNoTracking().Include(a => a.Room).ThenInclude(r => r.Accommodation)
                    .Where(a => a.userId == CurrentUserId && a.IsActive).ToListAsync(),
                Outstanding = await _billing.OutstandingAsync(CurrentUserId),
                PendingAmount = await _billing.PendingAmountAsync(CurrentUserId),
                OpenCharges = (await _db.charges.AsNoTracking().Where(c => c.AccountId == account.AccountId && c.IssueDate <= today).ToListAsync())
                    .Where(c => !c.IsPaid && !pendingIds.Contains(c.ChargeId)).OrderBy(c => c.DueDate).Take(5).ToList(),
                RecentPayments = await _db.payments.AsNoTracking().Where(p => p.AccountId == account.AccountId)
                    .OrderByDescending(p => p.DateOfPay).Take(5).ToListAsync(),
                Applications = await _db.applications.AsNoTracking().Include(a => a.Rooms).ThenInclude(r => r.Room).ThenInclude(r => r.Accommodation)
                    .Where(a => a.userId == CurrentUserId).OrderByDescending(a => a.DateSubmitted).Take(3).ToListAsync(),
                OpenMaintenance = await _db.maintenanceRequests.CountAsync(m => m.userId == CurrentUserId &&
                    (m.Status == MaintenanceStatuses.Submitted || m.Status == MaintenanceStatuses.InProgress)),
                IsSuspended = AccountStatuses.IsSuspended(account.AccountStatus)
            };
            model.HasPendingAppeal = model.IsSuspended && await _db.appeals.AnyAsync(a => a.userId == CurrentUserId && a.Status == AppealStatuses.Pending);
            return View(model);
        }

        public async Task<IActionResult> Profile()
        {
            var user = await _db.users.FirstOrDefaultAsync(u => u.UserId == CurrentUserId);
            if (user == null) return NotFound();
            await LoadProfileViewDataAsync(user);
            return View(new EditUserViewModel
            {
                UserId = user.UserId, UserName = user.UserName, UserSurname = user.UserSurname,
                UserEmail = user.UserEmail, IdentificationNumber = user.IdentificationNumber, PhoneNumber = user.PhoneNumber
            });
        }

        [HttpGet]
        public async Task<IActionResult> ExitResidence()
        {
            ViewBag.ActiveAllocations = await _db.roomAllocations.AsNoTracking()
                .Include(a => a.Room).ThenInclude(r => r.Accommodation)
                .Where(a => a.userId == CurrentUserId && a.IsActive).ToListAsync();
            ViewBag.Requests = await _db.residenceExitRequests.AsNoTracking()
                .Include(r => r.RoomAllocation).ThenInclude(a => a.Room).ThenInclude(r => r.Accommodation)
                .Where(r => r.userId == CurrentUserId).OrderByDescending(r => r.DateSubmitted).ToListAsync();
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RequestExit(int roomAllocationId, string reason)
        {
            var allocation = await _db.roomAllocations.Include(a => a.Room).ThenInclude(r => r.Accommodation)
                .FirstOrDefaultAsync(a => a.RoomAllocationId == roomAllocationId && a.userId == CurrentUserId && a.IsActive);
            if (allocation == null) return NotFound();
            reason = reason?.Trim() ?? "";
            if (reason.Length < 10 || reason.Length > 2000)
            {
                TempData["Error"] = "Please enter a reason between 10 and 2000 characters.";
                return RedirectToAction(nameof(ExitResidence));
            }
            if (await _db.residenceExitRequests.AnyAsync(r => r.RoomAllocationId == roomAllocationId && r.Status == ResidenceExitRequestStatuses.Pending))
            {
                TempData["Error"] = "There is already a pending exit request for this room.";
                return RedirectToAction(nameof(ExitResidence));
            }
            _db.residenceExitRequests.Add(new ResidenceExitRequest
            {
                RoomAllocationId = allocation.RoomAllocationId, userId = CurrentUserId,
                Reason = reason, Status = ResidenceExitRequestStatuses.Pending, DateSubmitted = DateTime.UtcNow
            });
            Notify.To(_db, CurrentUserId, "Your residence exit request has been submitted and is waiting for review.", "info");
            await Notify.ToStaffAsync(_db, $"{User.Identity?.Name ?? "A customer"} has requested to leave {allocation.Room.Accommodation.AccommodationName}, room {allocation.Room.RoomNumber}.", "info");
            await _db.SaveChangesAsync();
            TempData["Success"] = "Your exit request has been submitted for review.";
            return RedirectToAction(nameof(ExitResidence));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [BlockWhenSuspended]
        public async Task<IActionResult> Profile(EditUserViewModel model)
        {
            if (model.UserId != CurrentUserId) return Forbid();
            var user = await _db.users.FirstOrDefaultAsync(u => u.UserId == CurrentUserId);
            if (user == null) return NotFound();
            model.UserEmail = user.UserEmail; model.IdentificationNumber = user.IdentificationNumber;
            model.PhoneNumber = model.PhoneNumber?.Replace(" ", "").Trim() ?? "";
            if (!System.Text.RegularExpressions.Regex.IsMatch(model.PhoneNumber, RegisterViewModel.PhonePattern))
                ModelState.AddModelError(nameof(model.PhoneNumber), RegisterViewModel.PhoneError);
            if (!ModelState.IsValid)
            {
                await LoadProfileViewDataAsync(user);
                return View(model);
            }

            user.UserName = model.UserName.Trim();
            user.UserSurname = model.UserSurname.Trim();
            user.PhoneNumber = model.PhoneNumber;
            Notify.To(_db, user.UserId, "Your profile details were updated.", "info");
            await _db.SaveChangesAsync();

            var authResult = await HttpContext.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            var claims = User.Claims.Where(c => c.Type != ClaimTypes.Name).ToList();
            claims.Add(new Claim(ClaimTypes.Name, user.FullName));
            await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
                new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme)), authResult.Properties);

            TempData["Success"] = "Profile updated.";
            return RedirectToAction(nameof(Profile));
        }

        private async Task LoadProfileViewDataAsync(User user)
        {
            ViewBag.Rooms = await _db.roomAllocations.Include(a => a.Room).ThenInclude(r => r.Accommodation)
                .Where(a => a.userId == user.UserId && a.IsActive).ToListAsync();
            ViewBag.IsSuspended = AccountStatuses.IsSuspended(await _db.accounts
                .Where(a => a.userId == user.UserId).Select(a => a.AccountStatus).FirstOrDefaultAsync());
            ViewBag.PersonType = user.PersonType;
            ViewBag.AccountNumber = await _db.accounts.Where(a => a.userId == user.UserId).Select(a => a.AccountNumber).FirstOrDefaultAsync();
        }

        [HttpGet]
        public IActionResult ChangePassword() => View();

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ChangePassword(string currentPassword, string newPassword, string confirmPassword)
        {
            if (newPassword != confirmPassword)
            {
                ModelState.AddModelError(string.Empty, "New passwords do not match.");
                return View();
            }
            var user = await _db.users.FindAsync(CurrentUserId);
            if (user == null) return NotFound();
            if (_hasher.VerifyHashedPassword(user, user.PassWord, currentPassword ?? "") == PasswordVerificationResult.Failed)
            {
                ModelState.AddModelError(string.Empty, "Current password is incorrect.");
                return View();
            }
            if (!PasswordPolicy.AddErrors(ModelState, string.Empty, newPassword, user.UserName, user.UserSurname, user.UserEmail, user.IdentificationNumber))
                return View();
            if (_hasher.VerifyHashedPassword(user, user.PassWord, newPassword) != PasswordVerificationResult.Failed)
            {
                ModelState.AddModelError(string.Empty, "Your new password must be different from your old password.");
                return View();
            }

            user.PassWord = _hasher.HashPassword(user, newPassword);
            Notify.To(_db, user.UserId, "Your password was changed. If this wasn't you, contact a financial officer immediately.", "info");
            await _db.SaveChangesAsync();
            TempData["Success"] = "Password changed successfully.";
            return RedirectToAction(nameof(Index));
        }

        [HttpGet]
        public async Task<IActionResult> Appeal() => View(await BuildAppealPageAsync());

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Appeal(CustomerAppealViewModel form)
        {
            var page = await BuildAppealPageAsync();
            if (!page.IsSuspended)
            {
                TempData["Success"] = "Your account is active - there is nothing to appeal.";
                return RedirectToAction(nameof(Index));
            }
            if (page.HasPendingAppeal)
            {
                TempData["Error"] = "You already have an appeal waiting for a decision. You will be notified once it has been reviewed.";
                return RedirectToAction(nameof(Appeal));
            }

            foreach (var key in ModelState.Keys.Where(k => !k.StartsWith("NewAppeal.AppealReason")).ToList()) ModelState.Remove(key);
            if (!ModelState.IsValid)
            {
                page.NewAppeal = form.NewAppeal;
                return View(page);
            }

            _db.appeals.Add(new Appeal { userId = CurrentUserId, AppealReason = form.NewAppeal.AppealReason.Trim(), Status = AppealStatuses.Pending, DateSubmitted = DateTime.UtcNow });
            Notify.To(_db, CurrentUserId, "Your appeal has been submitted. You will be notified of the outcome.", "success");
            await Notify.ToStaffAsync(_db, "A suspended customer has submitted an appeal. Review it under Suspensions & Appeals.", "info");
            await _db.SaveChangesAsync();
            TempData["Success"] = "Appeal submitted. You will receive a notification once it has been reviewed.";
            return RedirectToAction(nameof(Appeal));
        }

        private async Task<CustomerAppealViewModel> BuildAppealPageAsync()
        {
            var account = await _db.accounts.AsNoTracking().FirstOrDefaultAsync(a => a.userId == CurrentUserId);
            var appeals = await _db.appeals.AsNoTracking().Where(a => a.userId == CurrentUserId).OrderByDescending(a => a.DateSubmitted).ToListAsync();
            return new CustomerAppealViewModel
            {
                IsSuspended = AccountStatuses.IsSuspended(account?.AccountStatus),
                SuspensionReason = account?.SuspensionReason,
                DateSuspended = account?.DateSuspended,
                HasPendingAppeal = appeals.Any(a => a.Status == AppealStatuses.Pending),
                PreviousAppeals = appeals
            };
        }
    }
}
