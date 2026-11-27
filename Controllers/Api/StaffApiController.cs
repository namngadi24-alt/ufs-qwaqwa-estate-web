using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.RegularExpressions;
using UFSWebApp.Data;
using UFSWebApp.Infrastructure;
using UFSWebApp.Models;
using UFSWebApp.Models.ViewModels;

namespace UFSWebApp.Controllers.Api
{
    public class DecisionRequest { public string? Decision { get; set; } public string? Note { get; set; } }
    public class NoteRequest { public string? Note { get; set; } }
    public class TariffUpdateRequest { public decimal Rate { get; set; } public DateTime EffectiveDate { get; set; } }
    public class WaterRequest { public int CustomerUserId { get; set; } public DateTime Period { get; set; } public decimal UsageKl { get; set; } }
    public class ElectricityUsageRequest { public int CustomerUserId { get; set; } public decimal Units { get; set; } public string? Note { get; set; } }
    public class ImportRequest { public string? Kind { get; set; } public DateTime Period { get; set; } public string? Data { get; set; } }
    public class MaintenanceUpdateRequest { public string? Status { get; set; } public string? StaffNote { get; set; } }
    public class CustomerEditRequest { public string? FirstName { get; set; } public string? Surname { get; set; } public string? PhoneNumber { get; set; } }
    public class PasswordRequest { public string? NewPassword { get; set; } }

    /// <summary>Financial Officer functions (the Agent Head can use all of them too).</summary>
    [Route("api/staff")]
    [Authorize(Roles = "Admin,FinancialOfficer", AuthenticationSchemes = ApiTokenAuthenticationHandler.SchemeName)]
    public class StaffApiController : ApiBaseController
    {
        private readonly AppDbContext _db;
        private readonly AccommodationService _accommodation;
        private readonly PaymentService _payments;
        private readonly TariffService _tariffs;
        private readonly UtilityService _utilities;
        private readonly BillingService _billing;

        public StaffApiController(AppDbContext db, AccommodationService accommodation, PaymentService payments, TariffService tariffs,
            UtilityService utilities, BillingService billing)
        {
            _db = db; _accommodation = accommodation; _payments = payments; _tariffs = tariffs; _utilities = utilities; _billing = billing;
        }

        [HttpGet("dashboard")]
        public async Task<IActionResult> Dashboard() => Ok(await StatsService.GetAsync(_db));

        // ------------------------------------------------------------------ applications
        [HttpGet("applications")]
        public async Task<IActionResult> Applications(string? status = "Pending")
        {
            var q = _db.applications.AsNoTracking().Include(a => a.user)
                .Include(a => a.Rooms).ThenInclude(r => r.Room).ThenInclude(r => r.Accommodation).AsQueryable();
            if (!string.IsNullOrWhiteSpace(status) && status != "All") q = q.Where(a => a.Status == status);
            var list = await q.OrderByDescending(a => a.DateSubmitted).Take(200).ToListAsync();
            return Ok(list.Select(a => new
            {
                application = ApiMap.Application(a),
                customer = new { userId = a.userId, name = a.user.FullName, a.user.IdentificationNumber, a.user.PersonType, a.user.PhoneNumber, email = a.user.UserEmail }
            }));
        }

        [HttpPost("applications/{id:int}/decision")]
        public async Task<IActionResult> DecideApplication(int id, [FromBody] DecisionRequest request)
        {
            var decision = request.Decision?.Trim().ToLowerInvariant();
            if (decision != "approve" && decision != "reject") return Fail("Decision must be \"approve\" or \"reject\".");
            var (ok, message) = await _accommodation.DecideAsync(id, decision == "approve", request.Note, StaffName());
            return ok ? Ok(new { message }) : Fail(message);
        }

        // ------------------------------------------------------------------ residence exit requests
        [HttpGet("exit-requests")]
        public async Task<IActionResult> ExitRequests(string? status = "Pending")
        {
            var query = _db.residenceExitRequests.AsNoTracking().Include(r => r.user)
                .Include(r => r.RoomAllocation).ThenInclude(a => a.Room).ThenInclude(r => r.Accommodation).AsQueryable();
            if (!string.IsNullOrWhiteSpace(status) && status != "All") query = query.Where(r => r.Status == status);
            var items = await query.OrderBy(r => r.Status == ResidenceExitRequestStatuses.Pending ? 0 : 1).ThenByDescending(r => r.DateSubmitted).Take(300).ToListAsync();
            return Ok(items.Select(r => new
            {
                requestId = r.ResidenceExitRequestId, customer = r.user.FullName, identificationNumber = r.user.IdentificationNumber,
                email = r.user.UserEmail, phoneNumber = r.user.PhoneNumber,
                room = r.RoomAllocation.Room.Accommodation.AccommodationName + " - Room " + r.RoomAllocation.Room.RoomNumber,
                reason = r.Reason, status = r.Status, decisionNote = r.DecisionNote, dateSubmitted = r.DateSubmitted,
                dateDecided = r.DateDecided, decidedBy = r.DecidedBy
            }));
        }

        [HttpPost("exit-requests/{id:int}/decision")]
        public async Task<IActionResult> DecideExitRequest(int id, [FromBody] DecisionRequest request)
        {
            var decision = request.Decision?.Trim().ToLowerInvariant();
            if (decision != "approve" && decision != "reject") return Fail("Decision must be approve or reject.");
            if (decision == "reject" && string.IsNullOrWhiteSpace(request.Note)) return Fail("Please provide a reason when rejecting the request.");
            var exit = await _db.residenceExitRequests.Include(r => r.user).Include(r => r.RoomAllocation).ThenInclude(a => a.Room)
                .FirstOrDefaultAsync(r => r.ResidenceExitRequestId == id);
            if (exit == null) return NotFound(new { message = "Exit request not found." });
            if (exit.Status != ResidenceExitRequestStatuses.Pending) return Fail("This request has already been decided.");
            var approved = decision == "approve";
            if (approved && !exit.RoomAllocation.IsActive) return Fail("This room allocation is no longer active, so the request could not be approved.");
            exit.Status = approved ? ResidenceExitRequestStatuses.Approved : ResidenceExitRequestStatuses.Rejected;
            exit.DecisionNote = request.Note?.Trim();
            exit.DecidedBy = StaffName();
            exit.DateDecided = DateTime.UtcNow;
            if (approved)
            {
                exit.RoomAllocation.IsActive = false;
                exit.RoomAllocation.MoveOutDate = DateTime.UtcNow;
            }
            Notify.To(_db, exit.userId, approved
                ? "Your residence exit request was approved. Your room is now available for allocation."
                : $"Your residence exit request was rejected. Reason: {exit.DecisionNote}", approved ? "success" : "danger");
            await _db.SaveChangesAsync();
            return Ok(new { message = approved ? "Request approved; the room is available again." : "Request rejected." });
        }

        // ------------------------------------------------------------------ payments (accept / reject pending payments)
        [HttpGet("payments")]
        public async Task<IActionResult> Payments(string? status, string? purpose)
        {
            var q = _db.payments.AsNoTracking().Include(p => p.account).ThenInclude(a => a.user)
                .Include(p => p.Allocations).ThenInclude(a => a.Charge).AsQueryable();
            if (!string.IsNullOrWhiteSpace(status)) q = q.Where(p => p.PaymentStatus == status);
            if (!string.IsNullOrWhiteSpace(purpose)) q = q.Where(p => p.PaymentPurpose == purpose);
            var list = await q.OrderByDescending(p => p.DateOfPay).Take(300).ToListAsync();
            return Ok(list.Select(p => new
            {
                payment = ApiMap.Payment(p),
                customer = new { userId = p.account.userId, name = p.account.user.FullName, p.account.user.IdentificationNumber, accountNumber = p.account.AccountNumber }
            }));
        }

        [HttpPost("payments/{id:int}/accept")]
        public async Task<IActionResult> Accept(int id)
        {
            var (ok, message) = await _payments.AcceptAsync(id, StaffName());
            return ok ? Ok(new { message }) : Fail(message);
        }

        [HttpPost("payments/{id:int}/reject")]
        public async Task<IActionResult> Reject(int id, [FromBody] NoteRequest request)
        {
            var (ok, message) = await _payments.RejectAsync(id, request.Note, StaffName());
            return ok ? Ok(new { message }) : Fail(message);
        }

        // ------------------------------------------------------------------ tariffs and utilities
        [HttpGet("tariffs")]
        public async Task<IActionResult> Tariffs()
        {
            var list = await _db.tariffs.AsNoTracking().Include(t => t.Changes).OrderBy(t => t.Category).ThenBy(t => t.Name).ToListAsync();
            return Ok(list.Select(t => new
            {
                tariff = ApiMap.Tariff(t),
                history = t.Changes.OrderByDescending(c => c.EffectiveDate).Take(5).Select(c => new { c.OldRate, c.NewRate, effectiveDate = c.EffectiveDate, changedBy = c.ChangedBy })
            }));
        }

        [HttpPut("tariffs/{id:int}")]
        public async Task<IActionResult> UpdateTariff(int id, [FromBody] TariffUpdateRequest request)
        {
            var (ok, message) = await _tariffs.UpdateAsync(id, request.Rate, request.EffectiveDate, StaffName());
            return ok ? Ok(new { message }) : Fail(message);
        }

        [HttpPost("utilities/water")]
        public async Task<IActionResult> RecordWater([FromBody] WaterRequest request)
        {
            var (ok, message) = await _utilities.RecordWaterAsync(request.CustomerUserId, request.Period, request.UsageKl, StaffName());
            return ok ? Ok(new { message }) : Fail(message);
        }

        [HttpPost("utilities/electricity-usage")]
        public async Task<IActionResult> RecordElectricity([FromBody] ElectricityUsageRequest request)
        {
            var (ok, message) = await _utilities.RecordElectricityConsumptionAsync(request.CustomerUserId, request.Units, StaffName(), request.Note);
            return ok ? Ok(new { message }) : Fail(message);
        }

        /// <summary>Import lines "studentOrStaffNumber,value". kind = "water" or "electricity".</summary>
        [HttpPost("utilities/import")]
        public async Task<IActionResult> Import([FromBody] ImportRequest request)
        {
            if (request.Kind != "water" && request.Kind != "electricity") return Fail("Kind must be \"water\" or \"electricity\".");
            var results = await _utilities.ImportAsync(request.Kind, request.Data, request.Period, StaffName());
            return Ok(new { imported = results.Count(r => r.ok), skipped = results.Count(r => !r.ok), messages = results.Select(r => new { r.ok, r.message }) });
        }

        [HttpGet("utilities/customers")]
        public async Task<IActionResult> UtilityCustomers() =>
            Ok(await _db.users.AsNoTracking().Where(u => u.role.RoleName == "Customer" && u.Allocations.Any(a => a.IsActive))
                .OrderBy(u => u.UserSurname).Select(u => new { userId = u.UserId, name = u.UserName + " " + u.UserSurname, u.IdentificationNumber }).ToListAsync());

        // ------------------------------------------------------------------ maintenance
        [HttpGet("maintenance")]
        public async Task<IActionResult> Maintenance(string? status)
        {
            var q = _db.maintenanceRequests.AsNoTracking().Include(m => m.user).Include(m => m.Room).ThenInclude(r => r.Accommodation).AsQueryable();
            if (!string.IsNullOrWhiteSpace(status) && status != "All") q = q.Where(m => m.Status == status);
            var list = await q.OrderByDescending(m => m.DateSubmitted).Take(200).ToListAsync();
            return Ok(list.Select(m => new
            {
                request = ApiMap.Maintenance(m),
                customer = new { userId = m.userId, name = m.user.FullName, m.user.IdentificationNumber, m.user.PhoneNumber }
            }));
        }

        [HttpPut("maintenance/{id:int}")]
        public async Task<IActionResult> UpdateMaintenance(int id, [FromBody] MaintenanceUpdateRequest request)
        {
            var m = await _db.maintenanceRequests.FirstOrDefaultAsync(x => x.MaintenanceRequestId == id);
            if (m == null) return NotFound(new { message = "Request not found." });
            if (request.Status == null || !MaintenanceStatuses.All.Contains(request.Status)) return Fail("Invalid status.");
            var note = request.StaffNote?.Trim();
            if (note?.Length > 500) note = note[..500];
            m.Status = request.Status; m.StaffNote = note; m.DateUpdated = DateTime.UtcNow; m.UpdatedBy = User.Identity?.Name;
            Notify.To(_db, m.userId, $"Your maintenance request ({m.Category}) is now: {m.Status}." + (string.IsNullOrWhiteSpace(note) ? "" : $" Note: {note}"),
                m.Status is MaintenanceStatuses.Resolved or MaintenanceStatuses.Closed ? "success" : "info");
            await _db.SaveChangesAsync();
            return Ok(new { message = "Request updated and the customer was notified." });
        }

        // ------------------------------------------------------------------ customers
        [HttpGet("customers")]
        public async Task<IActionResult> Customers(string? search)
        {
            var q = _db.users.AsNoTracking().Include(u => u.accounts).Where(u => u.role.RoleName == "Customer");
            if (!string.IsNullOrWhiteSpace(search))
            {
                var s = search.Trim();
                q = q.Where(u => u.UserName.Contains(s) || u.UserSurname.Contains(s) || u.IdentificationNumber.Contains(s) || u.UserEmail.Contains(s));
            }
            var list = await q.OrderBy(u => u.UserSurname).Take(200).ToListAsync();
            return Ok(list.Select(u => new
            {
                userId = u.UserId, name = u.FullName, u.IdentificationNumber, u.PersonType, u.PhoneNumber, email = u.UserEmail,
                accountStatus = u.accounts.FirstOrDefault()?.AccountStatus, isDemo = u.IsDemo
            }));
        }

        [HttpGet("customers/{id:int}")]
        public async Task<IActionResult> CustomerDetails(int id)
        {
            var user = await _db.users.AsNoTracking().Include(u => u.role).FirstOrDefaultAsync(u => u.UserId == id && u.role.RoleName == "Customer");
            if (user == null) return NotFound(new { message = "Customer not found." });
            var acc = await _db.accounts.AsNoTracking().FirstAsync(a => a.userId == id);
            await _billing.EnsureChargesAsync(id);
            return Ok(new
            {
                user = ApiMap.User(user),
                account = new { acc.AccountNumber, status = acc.AccountStatus, electricityUnits = acc.ElectricityUnits },
                outstanding = await _billing.OutstandingAsync(id),
                rooms = await _db.roomAllocations.AsNoTracking().Include(a => a.Room).ThenInclude(r => r.Accommodation).Where(a => a.userId == id && a.IsActive)
                    .Select(a => new { roomId = a.RoomId, label = a.Room.Accommodation.AccommodationName + " - Room " + a.Room.RoomNumber, a.MoveInDate }).ToListAsync(),
                charges = (await _db.charges.AsNoTracking().Where(c => c.AccountId == acc.AccountId).ToListAsync()).OrderByDescending(c => c.BillingPeriod).Take(30).Select(ApiMap.Charge),
                payments = (await _db.payments.AsNoTracking().Include(p => p.Allocations).ThenInclude(a => a.Charge).Where(p => p.AccountId == acc.AccountId)
                    .OrderByDescending(p => p.DateOfPay).Take(15).ToListAsync()).Select(p => ApiMap.Payment(p)),
                applications = (await _db.applications.AsNoTracking().Include(a => a.Rooms).ThenInclude(r => r.Room).ThenInclude(r => r.Accommodation)
                    .Where(a => a.userId == id).OrderByDescending(a => a.DateSubmitted).ToListAsync()).Select(ApiMap.Application),
                electricity = (await _db.electricityTransactions.AsNoTracking().Where(e => e.AccountId == acc.AccountId)
                    .OrderByDescending(e => e.Date).Take(15).ToListAsync()).Select(e => new
                    {
                        type = e.TransactionType, units = e.Units, rate = e.Rate, amount = e.Amount, date = ApiMap.Utc(e.Date),
                        metroToken = e.MetroToken, metroTokenFormatted = MetroToken.Format(e.MetroToken)
                    }),
                isSuspended = AccountStatuses.IsSuspended(acc.AccountStatus), suspensionReason = acc.SuspensionReason
            });
        }

        [HttpPut("customers/{id:int}")]
        public async Task<IActionResult> EditCustomer(int id, [FromBody] CustomerEditRequest request)
        {
            var user = await _db.users.Include(u => u.role).FirstOrDefaultAsync(u => u.UserId == id && u.role.RoleName == "Customer");
            if (user == null) return NotFound(new { message = "Customer not found." });
            var phone = request.PhoneNumber?.Replace(" ", "").Trim() ?? "";
            if (string.IsNullOrWhiteSpace(request.FirstName) || string.IsNullOrWhiteSpace(request.Surname)) return Fail("First name and surname are required.");
            if (!Regex.IsMatch(phone, RegisterViewModel.PhonePattern)) return Fail(RegisterViewModel.PhoneError);
            user.UserName = request.FirstName.Trim(); user.UserSurname = request.Surname.Trim(); user.PhoneNumber = phone;
            Notify.To(_db, user.UserId, "Your profile details were updated by an administrator/financial officer.", "info");
            await _db.SaveChangesAsync();
            return Ok(new { message = "Customer details updated." });
        }

        [HttpDelete("customers/{id:int}")]
        public async Task<IActionResult> DeleteCustomer(int id)
        {
            var customer = await _db.users.Include(u => u.role)
                .FirstOrDefaultAsync(u => u.UserId == id && u.role.RoleName == "Customer");
            if (customer == null) return NotFound(new { message = "Customer not found." });
            await CustomerAccounts.DeleteAsync(_db, customer);
            return Ok(new { message = "Customer account and its linked records were deleted." });
        }

        [HttpPost("customers/{id:int}/reset-password")]
        public async Task<IActionResult> ResetCustomerPassword(int id, [FromBody] PasswordRequest request)
        {
            var user = await _db.users.Include(u => u.role).FirstOrDefaultAsync(u => u.UserId == id && u.role.RoleName == "Customer");
            if (user == null) return NotFound(new { message = "Customer not found." });
            var errors = PasswordPolicy.Validate(request.NewPassword, user.UserName, user.UserSurname, user.UserEmail, user.IdentificationNumber).ToList();
            if (errors.Count > 0) return BadRequest(new { message = errors[0], errors });
            user.PassWord = new PasswordHasher<User>().HashPassword(user, request.NewPassword!);
            user.FailedLoginAttempts = 0; user.IsLockedOut = false; user.LockoutEndUtc = null;
            Notify.To(_db, user.UserId, "Your password was reset by a financial officer/administrator. Please change it after signing in.", "warning");
            await _db.SaveChangesAsync();
            return Ok(new { message = "Password reset. The account is unlocked." });
        }

        [HttpGet("compliance")]
        public async Task<IActionResult> Compliance()
        {
            var today = DateTime.Today;
            var customers = await _db.users.AsNoTracking().Include(u => u.accounts)
                .Where(u => u.role.RoleName == "Customer" && u.Allocations.Any(a => a.IsActive)).OrderBy(u => u.UserSurname).ToListAsync();
            var open = (await _db.charges.AsNoTracking().Where(c => c.IssueDate <= today).ToListAsync()).Where(c => !c.IsPaid).ToList();
            return Ok(customers.Select(u =>
            {
                var accId = u.accounts.First().AccountId;
                var mine = open.Where(c => c.AccountId == accId).ToList();
                var overdue = mine.Where(c => c.DueDate.Date < today).ToList();
                return new { userId = u.UserId, name = u.FullName, u.IdentificationNumber, outstanding = mine.Sum(c => c.Balance), overdue = overdue.Sum(c => c.Balance), overdueCount = overdue.Count };
            }).OrderByDescending(r => r.overdue));
        }

        [HttpGet("rooms")]
        public async Task<IActionResult> Rooms() => Ok((await _accommodation.RoomsAsync()).Select(ApiMap.Room));
    }

    /// <summary>Agent Head only: officers, password-reset approvals and deleting customers.</summary>
    [Route("api/admin")]
    [Authorize(Roles = "Admin", AuthenticationSchemes = ApiTokenAuthenticationHandler.SchemeName)]
    public class AdminApiController : ApiBaseController
    {
        private readonly AppDbContext _db;
        public AdminApiController(AppDbContext db) => _db = db;

        [HttpGet("officers")]
        public async Task<IActionResult> Officers() =>
            Ok(await _db.users.AsNoTracking().Where(u => u.role.RoleName == "FinancialOfficer").OrderBy(u => u.UserSurname)
                .Select(u => new { userId = u.UserId, name = u.UserName + " " + u.UserSurname, firstName = u.UserName, surname = u.UserSurname, u.IdentificationNumber, email = u.UserEmail, u.PhoneNumber, isAuthorised = u.IsAuthorised, isLockedOut = u.IsLockedOut, isDemo = u.IsDemo }).ToListAsync());

        [HttpPost("officers")]
        public async Task<IActionResult> CreateOfficer([FromBody] RegisterRequest request)
        {
            if (request.Password != request.ConfirmPassword) return Fail("Passwords do not match.");
            var (user, errors) = await CustomerAccounts.CreateAsync(_db, new NewUserInput
            {
                FName = request.FirstName, Surname = request.Surname, PersonType = "FinancialOfficer", IdentificationNumber = request.IdentificationNumber,
                PhoneNumber = request.PhoneNumber, Email = request.Email, Password = request.Password
            }, true, $"Created by {User.Identity?.Name}.");
            return user == null ? BadRequest(new { message = errors.FirstOrDefault() ?? "Could not create the officer.", errors }) : Ok(new { message = "Financial officer created and authorised.", userId = user.UserId });
        }

        [HttpPut("officers/{id:int}")]
        public async Task<IActionResult> EditOfficer(int id, [FromBody] OfficerEditRequest request)
        {
            var officer = await _db.users.Include(u => u.role)
                .FirstOrDefaultAsync(u => u.UserId == id && u.role.RoleName == "FinancialOfficer");
            if (officer == null) return NotFound(new { message = "Financial officer not found." });
            var number = request.IdentificationNumber?.Trim() ?? "";
            var email = request.Email?.Trim().ToLowerInvariant() ?? "";
            var phone = request.PhoneNumber?.Replace(" ", "").Trim() ?? "";
            if (string.IsNullOrWhiteSpace(request.FirstName) || request.FirstName.Length > 50 ||
                string.IsNullOrWhiteSpace(request.Surname) || request.Surname.Length > 50)
                return Fail("First name and surname are required (maximum 50 characters each).");
            var idError = RegisterViewModel.IdentificationNumberError("FinancialOfficer", number);
            if (idError != null) return Fail(idError);
            var emailError = RegisterViewModel.EmailError(email, number);
            if (emailError != null) return Fail(emailError);
            if (!Regex.IsMatch(phone, RegisterViewModel.PhonePattern)) return Fail(RegisterViewModel.PhoneError);
            if (await _db.users.AnyAsync(u => u.UserId != id && u.IdentificationNumber == number)) return Fail("That staff number is already registered.");
            if (await _db.users.AnyAsync(u => u.UserId != id && u.UserEmail == email)) return Fail("That email address is already registered.");

            officer.UserName = request.FirstName.Trim();
            officer.UserSurname = request.Surname.Trim();
            officer.IdentificationNumber = number;
            officer.PhoneNumber = phone;
            officer.UserEmail = email;
            Notify.To(_db, officer.UserId, "Your financial officer profile details were updated by the administrator.", "info");
            await _db.SaveChangesAsync();
            return Ok(new { message = "Financial officer details updated." });
        }

        [HttpPost("officers/{id:int}/toggle-authorisation")]
        public async Task<IActionResult> ToggleOfficer(int id)
        {
            var officer = await _db.users.FirstOrDefaultAsync(u => u.UserId == id && u.role.RoleName == "FinancialOfficer");
            if (officer == null) return NotFound(new { message = "Officer not found." });
            officer.IsAuthorised = !officer.IsAuthorised;
            Notify.To(_db, officer.UserId, officer.IsAuthorised ? "The administrator has authorised your Financial Officer account." : "The administrator has revoked your Financial Officer access.", officer.IsAuthorised ? "success" : "danger");
            await _db.SaveChangesAsync();
            return Ok(new { message = $"{officer.FullName} is now {(officer.IsAuthorised ? "authorised" : "NOT authorised")}.", isAuthorised = officer.IsAuthorised });
        }

        [HttpDelete("officers/{id:int}")]
        public async Task<IActionResult> DeleteOfficer(int id)
        {
            var officer = await _db.users.FirstOrDefaultAsync(u => u.UserId == id && u.role.RoleName == "FinancialOfficer");
            if (officer == null) return NotFound(new { message = "Officer not found." });
            await CustomerAccounts.DeleteAsync(_db, officer);
            return Ok(new { message = "Financial officer deleted." });
        }

        [HttpDelete("customers/{id:int}")]
        public async Task<IActionResult> DeleteCustomer(int id)
        {
            var user = await _db.users.FirstOrDefaultAsync(u => u.UserId == id && u.role.RoleName == "Customer");
            if (user == null) return NotFound(new { message = "Customer not found." });
            await CustomerAccounts.DeleteAsync(_db, user);
            return Ok(new { message = "Customer account deleted." });
        }

        [HttpGet("reset-requests")]
        public async Task<IActionResult> ResetRequests(string? status = "Pending")
        {
            var q = _db.passwordResetRequests.AsNoTracking().Include(r => r.user).AsQueryable();
            if (!string.IsNullOrWhiteSpace(status) && status != "All") q = q.Where(r => r.Status == status);
            var list = await q.OrderByDescending(r => r.DateRequested).Take(100).ToListAsync();
            return Ok(list.Select(r => new
            {
                requestId = r.PasswordResetRequestId, officer = r.user.FullName, r.user.IdentificationNumber, reason = r.Reason, status = r.Status,
                requested = ApiMap.Utc(r.DateRequested), reviewedBy = r.ReviewedBy, adminNote = r.AdminNote, expiresUtc = ApiMap.UtcOrNull(r.ExpiresUtc)
            }));
        }

        [HttpPost("reset-requests/{id:int}/decision")]
        public async Task<IActionResult> DecideReset(int id, [FromBody] DecisionRequest request)
        {
            var r = await _db.passwordResetRequests.FirstOrDefaultAsync(x => x.PasswordResetRequestId == id);
            if (r == null) return NotFound(new { message = "Request not found." });
            if (r.Status != ResetRequestStatuses.Pending) return Fail("This request has already been decided.");
            var decision = request.Decision?.Trim().ToLowerInvariant();
            if (decision != "approve" && decision != "reject") return Fail("Decision must be \"approve\" or \"reject\".");
            if (decision == "reject" && string.IsNullOrWhiteSpace(request.Note)) return Fail("Please give a reason when rejecting a request.");
            r.AdminNote = request.Note?.Trim(); r.ReviewedBy = User.Identity?.Name; r.DateReviewed = DateTime.UtcNow;
            if (decision == "approve")
            {
                r.Status = ResetRequestStatuses.Approved; r.ExpiresUtc = DateTime.UtcNow.AddHours(24);
                Notify.To(_db, r.userId, "Your password reset request was approved. Complete the reset within 24 hours.", "success");
            }
            else
            {
                r.Status = ResetRequestStatuses.Rejected;
                Notify.To(_db, r.userId, $"Your password reset request was rejected. Reason: {r.AdminNote}", "danger");
            }
            await _db.SaveChangesAsync();
            return Ok(new { message = decision == "approve" ? "Request approved (valid for 24 hours)." : "Request rejected." });
        }
    }
}
