using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using UFSWebApp.Data;
using UFSWebApp.Infrastructure;
using UFSWebApp.Models;
using UFSWebApp.Models.ViewModels;

namespace UFSWebApp.Controllers
{
    public class AccountController : Controller
    {
        private readonly AppDbContext _db;
        private readonly LoginService _login;
        public AccountController(AppDbContext db, LoginService login) { _db = db; _login = login; }

        [HttpGet]
        public IActionResult Register()
        {
            if (User.Identity?.IsAuthenticated == true) return RedirectToAction("Index", "Home");
            return View(new RegisterViewModel());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Register(RegisterViewModel model)
        {
            if (model.PersonType != "Student" && model.PersonType != "Staff")
                ModelState.AddModelError(nameof(model.PersonType), "Please select Student or Staff.");
            if (!ModelState.IsValid) return View(model);
            var (user, errors) = await CustomerAccounts.CreateAsync(_db, new NewUserInput
            {
                FName = model.FName, Surname = model.Surname, PersonType = model.PersonType,
                IdentificationNumber = model.IdentificationNumber, PhoneNumber = model.PhoneNumber,
                Email = model.Email, Password = model.Password
            }, authorisedByAdmin: false, createdByText: "Your account and personal profile have been created.");
            if (user == null)
            {
                foreach (var e in errors) ModelState.AddModelError(string.Empty, e);
                return View(model);
            }
            TempData["Success"] = "Registration successful. Your account and profile are ready - please sign in.";
            return RedirectToAction(nameof(Login));
        }

        // ------------------------------------------------------------ login / logout
        [HttpGet]
        public IActionResult Login(string? returnUrl = null)
        {
            ViewData["ReturnUrl"] = returnUrl;
            return View(new LoginViewModel());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(LoginViewModel model, string? returnUrl = null)
        {
            ViewData["ReturnUrl"] = returnUrl;
            if (!ModelState.IsValid) return View(model);

            var result = await _login.CheckAsync(model.Email, model.Password);
            if (!result.Succeeded)
            {
                ModelState.AddModelError(string.Empty, result.Message!);
                return View(model);
            }

            var user = result.User!;
            await SignInUserAsync(user);

            if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl)) return Redirect(returnUrl);

            if (user.role.RoleName == "Customer")
            {
                var status = await _db.accounts.Where(a => a.userId == user.UserId).Select(a => a.AccountStatus).FirstOrDefaultAsync();
                if (AccountStatuses.IsSuspended(status))
                    TempData["Error"] = "Your account is currently suspended. Some functions are unavailable until it is reinstated. You can submit an appeal from your account page.";
            }

            return user.role.RoleName switch
            {
                "Admin" => RedirectToAction("Index", "Admin"),
                "FinancialOfficer" => RedirectToAction("Index", "FinancialOfficer"),
                _ => RedirectToAction("Index", "Customer"),
            };
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Logout()
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return RedirectToAction("Index", "Home");
        }

        public IActionResult AccessDenied() => View();

        // ------------------------------------------------------------ financial officer password reset workflow
        [HttpGet]
        public IActionResult ForgotPassword() => View(new ForgotPasswordViewModel());

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ForgotPassword(ForgotPasswordViewModel model)
        {
            if (!ModelState.IsValid) return View(model);

            var email = model.Email.Trim().ToLowerInvariant();
            var number = model.IdentificationNumber.Trim();
            var phone = model.PhoneNumber.Replace(" ", "").Trim();

            var officer = await _db.users.Include(u => u.role).FirstOrDefaultAsync(u =>
                u.UserEmail == email && u.IdentificationNumber == number && u.PhoneNumber == phone && u.role.RoleName == "FinancialOfficer");

            if (officer != null &&
                !await _db.passwordResetRequests.AnyAsync(r => r.userId == officer.UserId && r.Status == ResetRequestStatuses.Pending))
            {
                _db.passwordResetRequests.Add(new PasswordResetRequest { userId = officer.UserId, Reason = model.Reason.Trim() });
                await Notify.ToAdminsAsync(_db, $"Password reset request from Financial Officer {officer.FullName}. Review it under Reset requests.", "warning");
                await _db.SaveChangesAsync();
            }

            // The same answer is shown whether or not the details matched, so the form cannot be used to discover accounts.
            TempData["Success"] = "If the details you entered match a Financial Officer account, your request has been sent to the administrator. " +
                                  "Once it is approved, use 'Complete an approved reset' on the sign-in page.";
            return RedirectToAction(nameof(Login));
        }

        [HttpGet]
        public IActionResult CompleteReset() => View(new CompleteResetViewModel());

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CompleteReset(CompleteResetViewModel model)
        {
            if (!ModelState.IsValid) return View(model);

            var email = model.Email.Trim().ToLowerInvariant();
            var number = model.IdentificationNumber.Trim();
            var officer = await _db.users.Include(u => u.role).FirstOrDefaultAsync(u =>
                u.UserEmail == email && u.IdentificationNumber == number && u.role.RoleName == "FinancialOfficer");

            PasswordResetRequest? request = null;
            if (officer != null)
                request = await _db.passwordResetRequests
                    .Where(r => r.userId == officer.UserId && r.Status == ResetRequestStatuses.Approved)
                    .OrderByDescending(r => r.DateReviewed).FirstOrDefaultAsync();

            if (officer == null || request == null)
            {
                ModelState.AddModelError(string.Empty, "No approved reset request was found for these details.");
                return View(model);
            }
            if (request.ExpiresUtc < DateTime.UtcNow)
            {
                request.Status = ResetRequestStatuses.Expired;
                await _db.SaveChangesAsync();
                ModelState.AddModelError(string.Empty, "Your approved request has expired. Please submit a new request.");
                return View(model);
            }

            if (!PasswordPolicy.AddErrors(ModelState, string.Empty, model.NewPassword, officer.UserName, officer.UserSurname, officer.UserEmail, officer.IdentificationNumber))
                return View(model);

            var hasher = new PasswordHasher<User>();
            if (hasher.VerifyHashedPassword(officer, officer.PassWord, model.NewPassword) != PasswordVerificationResult.Failed)
            {
                ModelState.AddModelError(string.Empty, "Your new password must be different from your old password.");
                return View(model);
            }

            officer.PassWord = hasher.HashPassword(officer, model.NewPassword);
            officer.FailedLoginAttempts = 0;
            officer.IsLockedOut = false;
            officer.LockoutEndUtc = null;
            request.Status = ResetRequestStatuses.Completed;
            request.DateCompleted = DateTime.UtcNow;
            Notify.To(_db, officer.UserId, "Your password was changed successfully. If this was not you, tell the administrator immediately.", "success");
            await Notify.ToAdminsAsync(_db, $"{officer.FullName} completed their approved password reset.", "info");
            await _db.SaveChangesAsync();

            TempData["Success"] = "Your password has been changed. You can now sign in.";
            return RedirectToAction(nameof(Login));
        }

        private async Task SignInUserAsync(User user)
        {
            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, user.UserId.ToString()),
                new(ClaimTypes.Name, user.FullName),
                new(ClaimTypes.Email, user.UserEmail),
                new(ClaimTypes.Role, user.role?.RoleName ?? _db.roles.First(r => r.RoleId == user.RoleId).RoleName),
                new("IsDemo", user.IsDemo ? "true" : "false")
            };
            var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity),
                new AuthenticationProperties { IsPersistent = false, AllowRefresh = true });
        }
    }
}
