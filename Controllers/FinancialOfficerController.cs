using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using UFSWebApp.Data;
using UFSWebApp.Infrastructure;
using UFSWebApp.Models;

namespace UFSWebApp.Controllers
{
    public class ComplianceRow
    {
        public User Customer { get; set; }
        public decimal Outstanding { get; set; }
        public decimal Overdue { get; set; }
        public int OverdueCount { get; set; }
        public DateTime? LastPayment { get; set; }
    }

    public class UtilitiesViewModel
    {
        public List<User> Customers { get; set; } = new();
        public List<WaterReading> Water { get; set; } = new();
        public List<ElectricityTransaction> Electricity { get; set; } = new();
        public decimal WaterRate { get; set; }
        public decimal ElectricityRate { get; set; }
    }

    /// <summary>Financial Officer functions. The Admin (Agent Head) can also use every action here.</summary>
    [Authorize(Roles = "Admin,FinancialOfficer")]
    public class FinancialOfficerController : Controller
    {
        private readonly AppDbContext _db;
        private readonly AccommodationService _accommodation;
        private readonly TariffService _tariffs;
        private readonly UtilityService _utilities;
        private readonly BillingService _billing;

        public FinancialOfficerController(AppDbContext db, AccommodationService accommodation, TariffService tariffs,
            UtilityService utilities, BillingService billing)
        {
            _db = db; _accommodation = accommodation; _tariffs = tariffs; _utilities = utilities; _billing = billing;
        }

        private string CurrentName => User.FindFirstValue(ClaimTypes.Name) ?? "Staff";
        private string CurrentRoleText => User.IsInRole("Admin") ? $"{CurrentName} (Agent Head)" : $"{CurrentName} (Financial Officer)";

        public async Task<IActionResult> Index()
        {
            ViewBag.Stats = await StatsService.GetAsync(_db);
            ViewBag.Pending = await _db.applications.AsNoTracking().Include(a => a.user)
                .Include(a => a.Rooms).ThenInclude(r => r.Room).ThenInclude(r => r.Accommodation)
                .Where(a => a.Status == ApplicationStatuses.Pending).OrderBy(a => a.DateSubmitted).Take(6).ToListAsync();
            ViewBag.RecentPayments = await _db.payments.AsNoTracking().Include(p => p.account).ThenInclude(a => a.user)
                .OrderByDescending(p => p.DateOfPay).Take(8).ToListAsync();
            return View();
        }

        // ------------------------------------------------------------------ applications (Financial Officer / Admin approve or reject)
        public async Task<IActionResult> Applications(string? status = "Pending")
        {
            var q = _db.applications.AsNoTracking().Include(a => a.user)
                .Include(a => a.Rooms).ThenInclude(r => r.Room).ThenInclude(r => r.Accommodation).AsQueryable();
            if (!string.IsNullOrWhiteSpace(status) && status != "All") q = q.Where(a => a.Status == status);
            ViewBag.Status = status ?? "All";
            ViewBag.Rooms = (await _accommodation.RoomsAsync()).ToDictionary(r => r.Room.RoomId);
            return View(await q.OrderBy(a => a.Status == ApplicationStatuses.Pending ? 0 : 1).ThenByDescending(a => a.DateSubmitted).ToListAsync());
        }

        public async Task<IActionResult> ExitRequests(string? status = "Pending")
        {
            var q = _db.residenceExitRequests.AsNoTracking().Include(r => r.user)
                .Include(r => r.RoomAllocation).ThenInclude(a => a.Room).ThenInclude(r => r.Accommodation).AsQueryable();
            if (!string.IsNullOrWhiteSpace(status) && status != "All") q = q.Where(r => r.Status == status);
            ViewBag.Status = status ?? "All";
            return View(await q.OrderBy(r => r.Status == ResidenceExitRequestStatuses.Pending ? 0 : 1)
                .ThenByDescending(r => r.DateSubmitted).ToListAsync());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DecideExitRequest(int id, string decision, string? note)
        {
            decision = decision?.Trim().ToLowerInvariant() ?? "";
            if (decision != "approve" && decision != "reject")
            {
                TempData["Error"] = "Invalid decision.";
                return RedirectToAction(nameof(ExitRequests));
            }
            if (decision == "reject" && string.IsNullOrWhiteSpace(note))
            {
                TempData["Error"] = "Please provide a reason when rejecting the request.";
                return RedirectToAction(nameof(ExitRequests));
            }
            var request = await _db.residenceExitRequests.Include(r => r.user)
                .Include(r => r.RoomAllocation).ThenInclude(a => a.Room)
                .FirstOrDefaultAsync(r => r.ResidenceExitRequestId == id);
            if (request == null) return NotFound();
            if (request.Status != ResidenceExitRequestStatuses.Pending)
            {
                TempData["Error"] = "This request has already been decided.";
                return RedirectToAction(nameof(ExitRequests));
            }
            var approved = decision == "approve";
            if (approved && !request.RoomAllocation.IsActive)
            {
                TempData["Error"] = "This room allocation is no longer active, so the request could not be approved.";
                return RedirectToAction(nameof(ExitRequests));
            }
            request.Status = approved ? ResidenceExitRequestStatuses.Approved : ResidenceExitRequestStatuses.Rejected;
            request.DecisionNote = note?.Trim();
            request.DecidedBy = CurrentRoleText;
            request.DateDecided = DateTime.UtcNow;
            if (approved)
            {
                request.RoomAllocation.IsActive = false;
                request.RoomAllocation.MoveOutDate = DateTime.UtcNow;
            }
            Notify.To(_db, request.userId, approved
                ? "Your residence exit request was approved. Your room is now available for allocation."
                : $"Your residence exit request was rejected.{(string.IsNullOrWhiteSpace(request.DecisionNote) ? "" : " Reason: " + request.DecisionNote)}",
                approved ? "success" : "danger");
            await _db.SaveChangesAsync();
            TempData["Success"] = approved ? "Request approved; the room is available again." : "Request rejected.";
            return RedirectToAction(nameof(ExitRequests));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DecideApplication(int id, string decision, string? note)
        {
            decision = decision?.Trim().ToLowerInvariant() ?? "";
            if (decision != "approve" && decision != "reject")
            {
                TempData["Error"] = "Invalid decision.";
                return RedirectToAction(nameof(Applications));
            }
            var (ok, message) = await _accommodation.DecideAsync(id, decision == "approve", note, CurrentRoleText);
            TempData[ok ? "Success" : "Error"] = message;
            return RedirectToAction(nameof(Applications));
        }

        // ------------------------------------------------------------------ tariffs
        public async Task<IActionResult> Tariffs()
        {
            var tariffs = await _db.tariffs.AsNoTracking().Include(t => t.Changes).OrderBy(t => t.Category).ThenBy(t => t.Name).ToListAsync();
            return View(tariffs);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateTariff(int id, decimal rate, DateTime effectiveDate)
        {
            var (ok, message) = await _tariffs.UpdateAsync(id, rate, effectiveDate, CurrentRoleText);
            TempData[ok ? "Success" : "Error"] = message;
            return RedirectToAction(nameof(Tariffs));
        }

        // ------------------------------------------------------------------ water and electricity
        public async Task<IActionResult> Utilities()
        {
            var tariffs = await _billing.LoadTariffsAsync();
            var water = BillingService.Find(tariffs, TariffCategories.Water);
            var elec = BillingService.Find(tariffs, TariffCategories.Electricity);
            var model = new UtilitiesViewModel
            {
                Customers = await _db.users.AsNoTracking().Where(u => u.role.RoleName == "Customer" && u.Allocations.Any(a => a.IsActive))
                    .OrderBy(u => u.UserSurname).ToListAsync(),
                Water = await _db.waterReadings.AsNoTracking().Include(w => w.Account).ThenInclude(a => a.user).OrderByDescending(w => w.DateRecorded).Take(12).ToListAsync(),
                Electricity = await _db.electricityTransactions.AsNoTracking().Include(e => e.Account).ThenInclude(a => a.user).OrderByDescending(e => e.Date).Take(12).ToListAsync(),
                WaterRate = water == null ? 0 : BillingService.RateOn(water, DateTime.Today),
                ElectricityRate = elec == null ? 0 : BillingService.RateOn(elec, DateTime.Today)
            };
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RecordWater(int userId, DateTime period, decimal usageKl)
        {
            var (ok, message) = await _utilities.RecordWaterAsync(userId, period, usageKl, CurrentRoleText);
            TempData[ok ? "Success" : "Error"] = message;
            return RedirectToAction(nameof(Utilities));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RecordElectricity(int userId, decimal units, string? note)
        {
            var (ok, message) = await _utilities.RecordElectricityConsumptionAsync(userId, units, CurrentRoleText, note);
            TempData[ok ? "Success" : "Error"] = message;
            return RedirectToAction(nameof(Utilities));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ImportReadings(string kind, DateTime period, string? data)
        {
            if (kind != "water" && kind != "electricity") { TempData["Error"] = "Invalid import type."; return RedirectToAction(nameof(Utilities)); }
            var results = await _utilities.ImportAsync(kind, data, period, CurrentRoleText);
            var okCount = results.Count(r => r.ok);
            var errors = results.Where(r => !r.ok).Select(r => r.message).Take(5).ToList();
            TempData["Success"] = $"Imported {okCount} reading(s).";
            if (errors.Count > 0) TempData["Error"] = $"{results.Count - okCount} line(s) were skipped: " + string.Join(" | ", errors);
            return RedirectToAction(nameof(Utilities));
        }

        // ------------------------------------------------------------------ compliance
        public async Task<IActionResult> Compliance()
        {
            var today = DateTime.Today;
            var customers = await _db.users.AsNoTracking().Include(u => u.accounts)
                .Where(u => u.role.RoleName == "Customer" && u.Allocations.Any(a => a.IsActive)).OrderBy(u => u.UserSurname).ToListAsync();
            var charges = (await _db.charges.AsNoTracking().Where(c => c.IssueDate <= today).ToListAsync()).Where(c => !c.IsPaid).ToList();
            var lastPay = await _db.payments.Where(p => p.PaymentStatus == PaymentStatuses.Successful)
                .GroupBy(p => p.AccountId).Select(g => new { g.Key, Last = g.Max(p => p.DateOfPay) }).ToDictionaryAsync(x => x.Key, x => x.Last);
            var rows = customers.Select(u =>
            {
                var acc = u.accounts.First();
                var mine = charges.Where(c => c.AccountId == acc.AccountId).ToList();
                var overdue = mine.Where(c => c.DueDate.Date < today).ToList();
                return new ComplianceRow
                {
                    Customer = u, Outstanding = mine.Sum(c => c.Balance), Overdue = overdue.Sum(c => c.Balance), OverdueCount = overdue.Count,
                    LastPayment = lastPay.TryGetValue(acc.AccountId, out var d) ? d : null
                };
            }).OrderByDescending(r => r.Overdue).ThenByDescending(r => r.Outstanding).ToList();
            return View(rows);
        }

        public async Task<IActionResult> Reviews()
        {
            var reviews = await _db.reviews.AsNoTracking().Include(r => r.user).OrderByDescending(r => r.DateSubmitted).ToListAsync();
            return View(reviews);
        }
    }
}
