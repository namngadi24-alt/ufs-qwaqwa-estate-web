using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using UFSWebApp.Data;
using UFSWebApp.Infrastructure;
using UFSWebApp.Models;
using UFSWebApp.Models.ViewModels;

namespace UFSWebApp.Controllers
{
    /// <summary>Administrator (Agent Head) only. There is exactly one administrator-level role.</summary>
    [Authorize(Roles = "Admin")]
    public class AdminController : Controller
    {
        private readonly AppDbContext _db;
        public AdminController(AppDbContext db) => _db = db;
        private string CurrentName => User.FindFirstValue(ClaimTypes.Name) ?? "Admin";

        public async Task<IActionResult> Index()
        {
            ViewBag.Stats = await StatsService.GetAsync(_db);
            ViewBag.PendingOfficers = await _db.users.AsNoTracking().Where(u => u.role.RoleName == "FinancialOfficer" && !u.IsAuthorised).ToListAsync();
            ViewBag.PendingApplications = await _db.applications.CountAsync(a => a.Status == ApplicationStatuses.Pending);
            ViewBag.OpenMaintenance = await _db.maintenanceRequests.CountAsync(m => m.Status == MaintenanceStatuses.Submitted);
            return View();
        }

        // ------------------------------------------------------------------ financial officers
        public async Task<IActionResult> FinancialOfficers()
        {
            return View(await _db.users.AsNoTracking().Where(u => u.role.RoleName == "FinancialOfficer").OrderBy(u => u.UserSurname).ToListAsync());
        }

        [HttpGet]
        public async Task<IActionResult> EditFinancialOfficer(int id)
        {
            var officer = await _db.users.Include(u => u.role)
                .FirstOrDefaultAsync(u => u.UserId == id && u.role.RoleName == "FinancialOfficer");
            if (officer == null) return NotFound();
            return View(new EditFinancialOfficerViewModel
            {
                UserId = officer.UserId, FirstName = officer.UserName, Surname = officer.UserSurname,
                IdentificationNumber = officer.IdentificationNumber, PhoneNumber = officer.PhoneNumber, Email = officer.UserEmail
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditFinancialOfficer(EditFinancialOfficerViewModel model)
        {
            var officer = await _db.users.Include(u => u.role)
                .FirstOrDefaultAsync(u => u.UserId == model.UserId && u.role.RoleName == "FinancialOfficer");
            if (officer == null) return NotFound();
            model.FirstName = model.FirstName?.Trim() ?? "";
            model.Surname = model.Surname?.Trim() ?? "";
            model.IdentificationNumber = model.IdentificationNumber?.Trim() ?? "";
            model.PhoneNumber = model.PhoneNumber?.Replace(" ", "").Trim() ?? "";
            model.Email = model.Email?.Trim().ToLowerInvariant() ?? "";
            if (await _db.users.AnyAsync(u => u.UserId != officer.UserId && u.IdentificationNumber == model.IdentificationNumber))
                ModelState.AddModelError(nameof(model.IdentificationNumber), "That staff number is already registered.");
            if (await _db.users.AnyAsync(u => u.UserId != officer.UserId && u.UserEmail == model.Email))
                ModelState.AddModelError(nameof(model.Email), "That email address is already registered.");
            if (!ModelState.IsValid) return View(model);

            officer.UserName = model.FirstName;
            officer.UserSurname = model.Surname;
            officer.IdentificationNumber = model.IdentificationNumber;
            officer.PhoneNumber = model.PhoneNumber;
            officer.UserEmail = model.Email;
            Notify.To(_db, officer.UserId, "Your financial officer profile details were updated by the administrator.", "info");
            await _db.SaveChangesAsync();
            TempData["Success"] = $"Financial officer details updated for {officer.FullName}.";
            return RedirectToAction(nameof(FinancialOfficers));
        }

        [HttpGet]
        public IActionResult CreateFinancialOfficer() => View(new CreateFinancialOfficerViewModel());

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateFinancialOfficer(CreateFinancialOfficerViewModel model)
        {
            if (!ModelState.IsValid) return View(model);
            var (user, errors) = await CustomerAccounts.CreateAsync(_db, new NewUserInput
            {
                FName = model.FName, Surname = model.Surname, PersonType = "FinancialOfficer", IdentificationNumber = model.IdentificationNumber,
                PhoneNumber = model.PhoneNumber, Email = model.Email, Password = model.Password
            }, true, $"Created by {CurrentName}.");
            if (user == null)
            {
                foreach (var e in errors) ModelState.AddModelError(string.Empty, e);
                return View(model);
            }
            TempData["Success"] = $"Financial officer {user.FullName} created and authorised.";
            return RedirectToAction(nameof(FinancialOfficers));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleOfficerAuthorisation(int id)
        {
            var officer = await _db.users.FirstOrDefaultAsync(u => u.UserId == id && u.role.RoleName == "FinancialOfficer");
            if (officer == null) return NotFound();
            officer.IsAuthorised = !officer.IsAuthorised;
            Notify.To(_db, officer.UserId, officer.IsAuthorised
                ? "The administrator has authorised your Financial Officer account."
                : "The administrator has revoked your Financial Officer access.", officer.IsAuthorised ? "success" : "danger");
            await _db.SaveChangesAsync();
            TempData["Success"] = $"{officer.FullName} is now {(officer.IsAuthorised ? "authorised" : "NOT authorised")}.";
            return RedirectToAction(nameof(FinancialOfficers));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteOfficer(int id)
        {
            var officer = await _db.users.FirstOrDefaultAsync(u => u.UserId == id && u.role.RoleName == "FinancialOfficer");
            if (officer == null) return NotFound();
            await CustomerAccounts.DeleteAsync(_db, officer);
            TempData["Success"] = "Financial officer deleted.";
            return RedirectToAction(nameof(FinancialOfficers));
        }

        // ------------------------------------------------------------------ password reset requests
        public async Task<IActionResult> ResetRequests(string? status = "Pending")
        {
            var now = DateTime.UtcNow;
            foreach (var expired in await _db.passwordResetRequests.Where(r => r.Status == ResetRequestStatuses.Approved && r.ExpiresUtc < now).ToListAsync())
                expired.Status = ResetRequestStatuses.Expired;
            await _db.SaveChangesAsync();

            var q = _db.passwordResetRequests.AsNoTracking().Include(r => r.user).AsQueryable();
            if (!string.IsNullOrWhiteSpace(status) && status != "All") q = q.Where(r => r.Status == status);
            ViewBag.Status = status ?? "All";
            return View(await q.OrderByDescending(r => r.DateRequested).ToListAsync());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DecideReset(int id, string decision, string? note)
        {
            var request = await _db.passwordResetRequests.Include(r => r.user).FirstOrDefaultAsync(r => r.PasswordResetRequestId == id);
            if (request == null) return NotFound();
            if (request.Status != ResetRequestStatuses.Pending)
            {
                TempData["Error"] = "This request has already been decided.";
                return RedirectToAction(nameof(ResetRequests));
            }
            decision = decision?.Trim().ToLowerInvariant() ?? "";
            if (decision != "approve" && decision != "reject")
            {
                TempData["Error"] = "Invalid decision.";
                return RedirectToAction(nameof(ResetRequests));
            }
            if (decision == "reject" && string.IsNullOrWhiteSpace(note))
            {
                TempData["Error"] = "Please give a reason when rejecting a request.";
                return RedirectToAction(nameof(ResetRequests));
            }

            request.AdminNote = note?.Trim();
            request.ReviewedBy = CurrentName;
            request.DateReviewed = DateTime.UtcNow;
            if (decision == "approve")
            {
                request.Status = ResetRequestStatuses.Approved;
                request.ExpiresUtc = DateTime.UtcNow.AddHours(24);
                Notify.To(_db, request.userId, "Your password reset request was approved. Use 'Complete an approved reset' on the sign-in page within 24 hours.", "success");
            }
            else
            {
                request.Status = ResetRequestStatuses.Rejected;
                Notify.To(_db, request.userId, $"Your password reset request was rejected. Reason: {request.AdminNote}", "danger");
            }
            await _db.SaveChangesAsync();
            TempData["Success"] = $"Request {(decision == "approve" ? "approved (valid for 24 hours)" : "rejected")}.";
            return RedirectToAction(nameof(ResetRequests));
        }
    }
}
