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
    [Authorize]
    public class PaymentController : Controller
    {
        private readonly AppDbContext _db;
        private readonly PaymentService _payments;
        private readonly BillingService _billing;

        public PaymentController(AppDbContext db, PaymentService payments, BillingService billing)
        {
            _db = db; _payments = payments; _billing = billing;
        }

        private int CurrentUserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        // ------------------------------------------------------------------ pay bills (single or bulk)
        [Authorize(Roles = "Customer")]
        [BlockWhenSuspended]
        [HttpGet]
        public async Task<IActionResult> Pay()
        {
            if (!await HasRoomAsync()) return RedirectToAction("Index", "Customer");
            return View(await BuildPayModelAsync(null));
        }

        [Authorize(Roles = "Customer")]
        [BlockWhenSuspended]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Pay(PayViewModel form)
        {
            if (!await HasRoomAsync()) return RedirectToAction("Index", "Customer");

            var outcome = await _payments.PayBillsAsync(CurrentUserId, form.SelectedKeys, new CardInput
            {
                CardHolder = form.CardHolder, CardNumber = form.CardNumber, Expiry = form.Expiry, Cvv = form.Cvv
            });

            if (!outcome.Success)
            {
                foreach (var e in outcome.Errors) ModelState.AddModelError(string.Empty, e);
                var model = await BuildPayModelAsync(form);
                model.CardNumber = null; model.Cvv = null;
                ModelState.Remove(nameof(form.CardNumber)); ModelState.Remove(nameof(form.Cvv));
                return View(model);
            }
            if (outcome.Warnings.Count > 0) TempData["Warning"] = string.Join(" ", outcome.Warnings.Distinct());
            TempData["Success"] = $"Payment of R{outcome.Payment!.Amount:N2} submitted. Its status is PENDING until a Financial Officer or the Agent Head accepts it.";
            return RedirectToAction(nameof(Receipt), new { id = outcome.Payment.PaymentId });
        }

        private async Task<PayViewModel> BuildPayModelAsync(PayViewModel? posted)
        {
            var user = await _db.users.AsNoTracking().FirstAsync(u => u.UserId == CurrentUserId);
            var account = await _db.accounts.AsNoTracking().FirstAsync(a => a.userId == CurrentUserId);
            var payable = await _billing.GetPayableAsync(CurrentUserId);
            var rooms = await _db.roomAllocations.AsNoTracking().Include(a => a.Room).ThenInclude(r => r.Accommodation)
                .Where(a => a.userId == CurrentUserId && a.IsActive).ToListAsync();
            return new PayViewModel
            {
                Items = payable.Items,
                Warnings = payable.Warnings,
                SelectedKeys = posted?.SelectedKeys ?? new List<string>(),
                Outstanding = await _billing.OutstandingAsync(CurrentUserId),
                ElectricityUnits = account.ElectricityUnits,
                AccountNumber = account.AccountNumber,
                CustomerName = user.FullName,
                IdentificationNumber = user.IdentificationNumber,
                Room = string.Join(", ", rooms.Select(r => r.Room.Label)),
                CardHolder = posted?.CardHolder,
                Expiry = posted?.Expiry
            };
        }

        // ------------------------------------------------------------------ buy electricity units
        [Authorize(Roles = "Customer")]
        [BlockWhenSuspended]
        [HttpGet]
        public async Task<IActionResult> Electricity()
        {
            if (!await HasRoomAsync()) return RedirectToAction("Index", "Customer");
            return View(await BuildElectricityModelAsync(null));
        }

        [Authorize(Roles = "Customer")]
        [BlockWhenSuspended]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Electricity(ElectricityViewModel form)
        {
            if (!await HasRoomAsync()) return RedirectToAction("Index", "Customer");

            if (!ModelState.IsValid) return View(await BuildElectricityModelAsync(form));

            var outcome = await _payments.BuyElectricityAsync(CurrentUserId, form.Units, new CardInput
            {
                CardHolder = form.CardHolder, CardNumber = form.CardNumber, Expiry = form.Expiry, Cvv = form.Cvv
            });
            if (!outcome.Success)
            {
                foreach (var e in outcome.Errors) ModelState.AddModelError(string.Empty, e);
                var model = await BuildElectricityModelAsync(form);
                model.CardNumber = null; model.Cvv = null;
                ModelState.Remove(nameof(form.CardNumber)); ModelState.Remove(nameof(form.Cvv));
                return View(model);
            }
            TempData["Success"] = $"Electricity purchased: {form.Units:N2} units for R{outcome.Payment!.Amount:N2}. Your Metro Token is on the receipt.";
            return RedirectToAction(nameof(Receipt), new { id = outcome.Payment.PaymentId });
        }

        private async Task<ElectricityViewModel> BuildElectricityModelAsync(ElectricityViewModel? posted)
        {
            var user = await _db.users.AsNoTracking().FirstAsync(u => u.UserId == CurrentUserId);
            var account = await _db.accounts.AsNoTracking().FirstAsync(a => a.userId == CurrentUserId);
            var tariffs = await _billing.LoadTariffsAsync();
            var tariff = BillingService.Find(tariffs, TariffCategories.Electricity);
            return new ElectricityViewModel
            {
                Units = posted?.Units > 0 ? posted.Units : 50,
                Rate = tariff == null ? 0 : BillingService.RateOn(tariff, DateTime.Today),
                CurrentUnits = account.ElectricityUnits,
                AccountNumber = account.AccountNumber,
                CustomerName = user.FullName,
                IdentificationNumber = user.IdentificationNumber,
                Recent = await _db.electricityTransactions.AsNoTracking().Where(e => e.AccountId == account.AccountId)
                    .OrderByDescending(e => e.Date).Take(8).ToListAsync(),
                CardHolder = posted?.CardHolder,
                Expiry = posted?.Expiry
            };
        }

        private async Task<bool> HasRoomAsync()
        {
            if (await _db.roomAllocations.AnyAsync(a => a.userId == CurrentUserId && a.IsActive)) return true;
            TempData["Error"] = "You can pay bills and buy electricity once your accommodation application has been approved and a room allocated.";
            return false;
        }

        // ------------------------------------------------------------------ receipts / history
        public async Task<IActionResult> Receipt(int id)
        {
            var q = _db.payments.Include(p => p.account).ThenInclude(a => a.user)
                .Include(p => p.Allocations).ThenInclude(a => a.Charge).AsQueryable();
            if (User.IsInRole("Customer")) q = q.Where(p => p.account.userId == CurrentUserId);
            var payment = await q.FirstOrDefaultAsync(p => p.PaymentId == id);
            if (payment == null) return NotFound();
            var token = await _db.electricityTransactions.AsNoTracking().Where(e => e.PaymentId == id).Select(e => e.MetroToken).FirstOrDefaultAsync();
            ViewBag.Token = token;
            return View(payment);
        }

        public async Task<IActionResult> History(string? status, string? purpose)
        {
            var query = _db.payments.AsNoTracking().Include(p => p.account).ThenInclude(a => a.user)
                .Include(p => p.Allocations).ThenInclude(a => a.Charge).AsQueryable();
            if (User.IsInRole("Customer"))
            {
                query = query.Where(p => p.account.userId == CurrentUserId);
                var acc = await _db.accounts.AsNoTracking().FirstOrDefaultAsync(a => a.userId == CurrentUserId);
                ViewBag.Outstanding = await _billing.OutstandingAsync(CurrentUserId);
                ViewBag.ElectricityUnits = acc?.ElectricityUnits ?? 0m;
                var today = DateTime.Today;
                var pendingIds = await _billing.PendingChargeIdsAsync(CurrentUserId);
                ViewBag.PendingAmount = await _billing.PendingAmountAsync(CurrentUserId);
                ViewBag.OpenCharges = acc == null ? new List<Charge>() :
                    (await _db.charges.AsNoTracking().Where(c => c.AccountId == acc.AccountId && c.IssueDate <= today).ToListAsync())
                    .Where(c => !c.IsPaid && !pendingIds.Contains(c.ChargeId)).OrderBy(c => c.DueDate).ToList();
            }
            if (!string.IsNullOrWhiteSpace(status)) query = query.Where(p => p.PaymentStatus == status);
            if (!string.IsNullOrWhiteSpace(purpose)) query = query.Where(p => p.PaymentPurpose == purpose);
            ViewBag.Status = status; ViewBag.Purpose = purpose;
            return View(await query.OrderByDescending(p => p.DateOfPay).Take(300).ToListAsync());
        }

        // ------------------------------------------------------------------ staff: accept or reject a pending payment
        [Authorize(Roles = "Admin,FinancialOfficer")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Accept(int id, string? returnTo)
        {
            var (ok, message) = await _payments.AcceptAsync(id, StaffName());
            TempData[ok ? "Success" : "Error"] = message;
            return RedirectAfterDecision(returnTo, id);
        }

        [Authorize(Roles = "Admin,FinancialOfficer")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Reject(int id, string? note, string? returnTo)
        {
            var (ok, message) = await _payments.RejectAsync(id, note, StaffName());
            TempData[ok ? "Success" : "Error"] = message;
            return RedirectAfterDecision(returnTo, id);
        }

        private string StaffName() =>
            $"{User.FindFirstValue(ClaimTypes.Name)} ({(User.IsInRole("Admin") ? "Agent Head" : "Financial Officer")})";

        private IActionResult RedirectAfterDecision(string? returnTo, int id) =>
            returnTo == "receipt" ? RedirectToAction(nameof(Receipt), new { id }) : RedirectToAction(nameof(History), new { status = PaymentStatuses.Pending });
    }
}
