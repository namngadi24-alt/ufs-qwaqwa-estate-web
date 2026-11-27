using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Text.RegularExpressions;
using UFSWebApp.Data;
using UFSWebApp.Infrastructure;
using UFSWebApp.Models;
using UFSWebApp.Models.ViewModels;

namespace UFSWebApp.Controllers
{
    public class CustomerDetailsViewModel
    {
        public User Customer { get; set; }
        public Account Account { get; set; }
        public List<RoomAllocation> Allocations { get; set; } = new();
        public List<Charge> Charges { get; set; } = new();
        public List<Payment> Payments { get; set; } = new();
        public List<AccommodationApplication> Applications { get; set; } = new();
        public List<ElectricityTransaction> Electricity { get; set; } = new();
        public decimal Outstanding { get; set; }
    }

    /// <summary>Customer account management shared by the Agent Head (Admin) and Financial Officers.</summary>
    [Authorize(Roles = "Admin,FinancialOfficer")]
    public class CustomersController : Controller
    {
        private readonly AppDbContext _db;
        private readonly BillingService _billing;
        private readonly PasswordHasher<User> _hasher = new();
        public CustomersController(AppDbContext db, BillingService billing) { _db = db; _billing = billing; }

        private async Task<User?> FindCustomerAsync(int id) =>
            await _db.users.Include(u => u.role).FirstOrDefaultAsync(u => u.UserId == id && u.role.RoleName == "Customer");

        public async Task<IActionResult> Index(string? search)
        {
            var q = _db.users.AsNoTracking().Include(u => u.accounts).Include(u => u.Allocations).ThenInclude(a => a.Room).ThenInclude(r => r.Accommodation)
                .Where(u => u.role.RoleName == "Customer");
            if (!string.IsNullOrWhiteSpace(search))
            {
                var s = search.Trim();
                q = q.Where(u => u.UserName.Contains(s) || u.UserSurname.Contains(s) || u.IdentificationNumber.Contains(s) || u.UserEmail.Contains(s));
            }
            ViewBag.Search = search;
            return View(await q.OrderBy(u => u.UserSurname).ThenBy(u => u.UserName).ToListAsync());
        }

        public async Task<IActionResult> Details(int id)
        {
            var user = await FindCustomerAsync(id);
            if (user == null) return NotFound();
            var account = await _db.accounts.FirstAsync(a => a.userId == id);
            await _billing.EnsureChargesAsync(id);
            var today = DateTime.Today;
            var model = new CustomerDetailsViewModel
            {
                Customer = user,
                Account = account,
                Allocations = await _db.roomAllocations.AsNoTracking().Include(a => a.Room).ThenInclude(r => r.Accommodation).Where(a => a.userId == id).OrderByDescending(a => a.MoveInDate).ToListAsync(),
                Charges = (await _db.charges.AsNoTracking().Where(c => c.AccountId == account.AccountId).ToListAsync()).OrderByDescending(c => c.BillingPeriod).ThenBy(c => c.ChargeType).Take(30).ToList(),
                Payments = await _db.payments.AsNoTracking().Where(p => p.AccountId == account.AccountId).OrderByDescending(p => p.DateOfPay).Take(15).ToListAsync(),
                Applications = await _db.applications.AsNoTracking().Include(a => a.Rooms).ThenInclude(r => r.Room).Where(a => a.userId == id).OrderByDescending(a => a.DateSubmitted).ToListAsync(),
                Electricity = await _db.electricityTransactions.AsNoTracking().Where(e => e.AccountId == account.AccountId).OrderByDescending(e => e.Date).Take(10).ToListAsync(),
                Outstanding = await _billing.OutstandingAsync(id)
            };
            return View(model);
        }

        [HttpGet]
        public IActionResult Create() => View(new RegisterViewModel());

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(RegisterViewModel model)
        {
            if (model.PersonType == "FinancialOfficer") ModelState.AddModelError(nameof(model.PersonType), "Customers are students or staff. Financial officers are created by the administrator.");
            if (!ModelState.IsValid) return View(model);

            var by = User.FindFirstValue(ClaimTypes.Name);
            var (user, errors) = await CustomerAccounts.CreateAsync(_db, new NewUserInput
            {
                FName = model.FName, Surname = model.Surname, PersonType = model.PersonType, IdentificationNumber = model.IdentificationNumber,
                PhoneNumber = model.PhoneNumber, Email = model.Email, Password = model.Password
            }, true, $"Your account was created by {by}.");
            if (user == null)
            {
                foreach (var e in errors) ModelState.AddModelError(string.Empty, e);
                return View(model);
            }
            TempData["Success"] = $"Customer account created for {user.FullName}.";
            return RedirectToAction(nameof(Index));
        }

        [HttpGet]
        public async Task<IActionResult> Edit(int id)
        {
            var user = await FindCustomerAsync(id);
            if (user == null) return NotFound();
            return View(new EditUserViewModel
            {
                UserId = user.UserId, UserName = user.UserName, UserSurname = user.UserSurname,
                UserEmail = user.UserEmail, IdentificationNumber = user.IdentificationNumber, PhoneNumber = user.PhoneNumber
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(EditUserViewModel model)
        {
            var user = await FindCustomerAsync(model.UserId);
            if (user == null) return NotFound();
            model.UserEmail = user.UserEmail; model.IdentificationNumber = user.IdentificationNumber;
            model.PhoneNumber = model.PhoneNumber?.Replace(" ", "").Trim() ?? "";
            if (!Regex.IsMatch(model.PhoneNumber, RegisterViewModel.PhonePattern))
                ModelState.AddModelError(nameof(model.PhoneNumber), RegisterViewModel.PhoneError);
            if (!ModelState.IsValid) return View(model);

            user.UserName = model.UserName.Trim();
            user.UserSurname = model.UserSurname.Trim();
            user.PhoneNumber = model.PhoneNumber;
            Notify.To(_db, user.UserId, "Your profile details were updated by an administrator/financial officer.", "info");
            await _db.SaveChangesAsync();
            TempData["Success"] = "Customer details updated.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            var user = await FindCustomerAsync(id);
            if (user == null) return NotFound();
            await CustomerAccounts.DeleteAsync(_db, user);
            TempData["Success"] = "Customer account deleted.";
            return RedirectToAction(nameof(Index));
        }

        [HttpGet]
        public async Task<IActionResult> ResetPassword(int id)
        {
            var user = await FindCustomerAsync(id);
            if (user == null) return NotFound();
            return View(new ResetPasswordViewModel { UserId = id, DisplayName = $"{user.FullName} ({user.IdentificationNumber})" });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ResetPassword(ResetPasswordViewModel model)
        {
            var user = await FindCustomerAsync(model.UserId);
            if (user == null) return NotFound();
            model.DisplayName = $"{user.FullName} ({user.IdentificationNumber})";
            if (!ModelState.IsValid) return View(model);
            if (!PasswordPolicy.AddErrors(ModelState, string.Empty, model.NewPassword, user.UserName, user.UserSurname, user.UserEmail, user.IdentificationNumber))
                return View(model);

            user.PassWord = _hasher.HashPassword(user, model.NewPassword);
            user.FailedLoginAttempts = 0; user.IsLockedOut = false; user.LockoutEndUtc = null;
            Notify.To(_db, user.UserId, "Your password was reset by a financial officer/administrator. Please change it after signing in.", "warning");
            await _db.SaveChangesAsync();
            TempData["Success"] = $"Password reset for {user.FullName}. The account is unlocked.";
            return RedirectToAction(nameof(Index));
        }
    }
}
