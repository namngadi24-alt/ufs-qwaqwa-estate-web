using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using UFSWebApp.Data;
using UFSWebApp.Infrastructure;
using UFSWebApp.Models;
using UFSWebApp.Models.ViewModels;

namespace UFSWebApp.Controllers.Api
{
    public class UpdateProfileRequest { public string? FirstName { get; set; } public string? Surname { get; set; } public string? PhoneNumber { get; set; } }
    public class ChangePasswordRequest { public string? CurrentPassword { get; set; } public string? NewPassword { get; set; } public string? ConfirmPassword { get; set; } }
    public class ApplicationRequest
    {
        public List<int> RoomIds { get; set; } = new();
        public string? PreferredRoomType { get; set; }
        public int NumberOfVehicles { get; set; }
        public DateTime? MoveInDate { get; set; }
        public string? Notes { get; set; }
        public bool AcceptRules { get; set; }
    }
    public class PayBillsRequest { public List<string> SelectedKeys { get; set; } = new(); public CardDto Card { get; set; } = new(); }
    public class BuyElectricityRequest { public decimal Units { get; set; } public CardDto Card { get; set; } = new(); }
    public class AppealRequest { public string? Reason { get; set; } }
    public class ReviewRequest { public int Rating { get; set; } public string? Description { get; set; } }
    public class CustomerExitRequest { public int RoomAllocationId { get; set; } public string? Reason { get; set; } }
    public class MaintenanceRequestForm
    {
        public int RoomId { get; set; }
        public string? Category { get; set; }
        public string? Description { get; set; }
        public IFormFile? Photo { get; set; }
    }

    /// <summary>Everything a student or staff customer can do on the website, as JSON for the Android app.</summary>
    [Route("api/customer")]
    [Authorize(Roles = "Customer", AuthenticationSchemes = ApiTokenAuthenticationHandler.SchemeName)]
    public class CustomerApiController : ApiBaseController
    {
        private readonly AppDbContext _db;
        private readonly BillingService _billing;
        private readonly PaymentService _payments;
        private readonly AccommodationService _accommodation;
        private readonly FileStorage _files;
        private readonly PasswordHasher<User> _hasher = new();

        public CustomerApiController(AppDbContext db, BillingService billing, PaymentService payments, AccommodationService accommodation, FileStorage files)
        {
            _db = db; _billing = billing; _payments = payments; _accommodation = accommodation; _files = files;
        }

        private async Task<bool> IsSuspendedAsync() =>
            AccountStatuses.IsSuspended(await _db.accounts.Where(a => a.userId == CurrentUserId).Select(a => a.AccountStatus).FirstOrDefaultAsync());

        private IActionResult Suspended() => Fail("Your account is suspended. Submit an appeal to be reinstated.", 403);

        // ------------------------------------------------------------------ dashboard + profile
        [HttpGet("dashboard")]
        public async Task<IActionResult> Dashboard()
        {
            var user = await _db.users.AsNoTracking().Include(u => u.role).FirstAsync(u => u.UserId == CurrentUserId);
            var account = await _db.accounts.AsNoTracking().FirstAsync(a => a.userId == CurrentUserId);
            await _billing.EnsureChargesAsync(CurrentUserId);
            await _billing.SendRemindersAsync(CurrentUserId);

            var today = DateTime.Today;
            var pendingIds = await _billing.PendingChargeIdsAsync(CurrentUserId);
            var allocations = await _db.roomAllocations.AsNoTracking().Include(a => a.Room).ThenInclude(r => r.Accommodation)
                .Where(a => a.userId == CurrentUserId && a.IsActive).ToListAsync();
            var open = (await _db.charges.AsNoTracking().Where(c => c.AccountId == account.AccountId && c.IssueDate <= today).ToListAsync())
                .Where(c => !c.IsPaid && !pendingIds.Contains(c.ChargeId)).OrderBy(c => c.DueDate).Take(10).ToList();
            var recent = await _db.payments.AsNoTracking().Include(p => p.Allocations).ThenInclude(a => a.Charge)
                .Where(p => p.AccountId == account.AccountId).OrderByDescending(p => p.DateOfPay).Take(5).ToListAsync();

            return Ok(new
            {
                user = ApiMap.User(user),
                account = new { account.AccountNumber, status = account.AccountStatus, electricityUnits = account.ElectricityUnits, suspensionReason = account.SuspensionReason },
                isSuspended = AccountStatuses.IsSuspended(account.AccountStatus),
                hasRoom = allocations.Count > 0,
                canReportMaintenance = allocations.Count > 0,
                outstanding = await _billing.OutstandingAsync(CurrentUserId),
                pendingAcceptance = await _billing.PendingAmountAsync(CurrentUserId),
                rooms = allocations.Select(a => new { roomId = a.RoomId, label = a.Room.Label, roomType = a.Room.RoomType, moveInDate = a.MoveInDate }),
                openCharges = open.Select(ApiMap.Charge),
                recentPayments = recent.Select(p => ApiMap.Payment(p)),
                openMaintenance = await _db.maintenanceRequests.CountAsync(m => m.userId == CurrentUserId &&
                    (m.Status == MaintenanceStatuses.Submitted || m.Status == MaintenanceStatuses.InProgress)),
                unreadNotifications = await _db.notifications.CountAsync(n => n.userId == CurrentUserId && !n.IsRead)
            });
        }

        [HttpGet("profile")]
        public async Task<IActionResult> Profile()
        {
            var user = await _db.users.AsNoTracking().Include(u => u.role).FirstAsync(u => u.UserId == CurrentUserId);
            var acc = await _db.accounts.AsNoTracking().FirstAsync(a => a.userId == CurrentUserId);
            return Ok(new { user = ApiMap.User(user), accountNumber = acc.AccountNumber, accountStatus = acc.AccountStatus });
        }

        [HttpPut("profile")]
        public async Task<IActionResult> UpdateProfile([FromBody] UpdateProfileRequest request)
        {
            if (await IsSuspendedAsync()) return Suspended();
            var user = await _db.users.FirstAsync(u => u.UserId == CurrentUserId);
            var phone = request.PhoneNumber?.Replace(" ", "").Trim() ?? "";
            if (string.IsNullOrWhiteSpace(request.FirstName) || string.IsNullOrWhiteSpace(request.Surname)) return Fail("First name and surname are required.");
            if (!Regex.IsMatch(phone, RegisterViewModel.PhonePattern)) return Fail(RegisterViewModel.PhoneError);
            user.UserName = request.FirstName.Trim(); user.UserSurname = request.Surname.Trim(); user.PhoneNumber = phone;
            Notify.To(_db, user.UserId, "Your profile details were updated.", "info");
            await _db.SaveChangesAsync();
            return Ok(new { message = "Profile updated.", user = ApiMap.User(user) });
        }

        [HttpPost("change-password")]
        public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request)
        {
            if (request.NewPassword != request.ConfirmPassword) return Fail("New passwords do not match.");
            var user = await _db.users.FirstAsync(u => u.UserId == CurrentUserId);
            if (_hasher.VerifyHashedPassword(user, user.PassWord, request.CurrentPassword ?? "") == PasswordVerificationResult.Failed)
                return Fail("Current password is incorrect.");
            var errors = PasswordPolicy.Validate(request.NewPassword, user.UserName, user.UserSurname, user.UserEmail, user.IdentificationNumber).ToList();
            if (errors.Count > 0) return BadRequest(new { message = errors[0], errors });
            if (_hasher.VerifyHashedPassword(user, user.PassWord, request.NewPassword!) != PasswordVerificationResult.Failed)
                return Fail("Your new password must be different from your old password.");
            user.PassWord = _hasher.HashPassword(user, request.NewPassword!);
            Notify.To(_db, user.UserId, "Your password was changed.", "info");
            await _db.SaveChangesAsync();
            return Ok(new { message = "Password changed successfully." });
        }

        // ------------------------------------------------------------------ applications
        [HttpGet("applications")]
        public async Task<IActionResult> Applications()
        {
            var apps = await _db.applications.AsNoTracking().Include(a => a.Rooms).ThenInclude(r => r.Room).ThenInclude(r => r.Accommodation)
                .Where(a => a.userId == CurrentUserId).OrderByDescending(a => a.DateSubmitted).ToListAsync();
            return Ok(apps.Select(ApiMap.Application));
        }

        // ------------------------------------------------------------------ residence exit requests
        [HttpGet("exit-requests")]
        public async Task<IActionResult> ExitRequests()
        {
            var allocations = await _db.roomAllocations.AsNoTracking().Include(a => a.Room).ThenInclude(r => r.Accommodation)
                .Where(a => a.userId == CurrentUserId && a.IsActive).OrderByDescending(a => a.MoveInDate)
                .Select(a => new { roomAllocationId = a.RoomAllocationId, room = a.Room.Accommodation.AccommodationName + " - Room " + a.Room.RoomNumber, roomType = a.Room.RoomType, moveInDate = a.MoveInDate })
                .ToListAsync();
            var requests = await _db.residenceExitRequests.AsNoTracking().Include(r => r.RoomAllocation).ThenInclude(a => a.Room).ThenInclude(r => r.Accommodation)
                .Where(r => r.userId == CurrentUserId).OrderByDescending(r => r.DateSubmitted)
                .Select(r => new { requestId = r.ResidenceExitRequestId, roomAllocationId = r.RoomAllocationId, room = r.RoomAllocation.Room.Accommodation.AccommodationName + " - Room " + r.RoomAllocation.Room.RoomNumber, reason = r.Reason, status = r.Status, decisionNote = r.DecisionNote, dateSubmitted = r.DateSubmitted, dateDecided = r.DateDecided })
                .ToListAsync();
            return Ok(new { allocations, requests });
        }

        [HttpPost("exit-requests")]
        public async Task<IActionResult> RequestExit([FromBody] CustomerExitRequest request)
        {
            if (await IsSuspendedAsync()) return Suspended();
            var allocation = await _db.roomAllocations.Include(a => a.Room).ThenInclude(r => r.Accommodation)
                .FirstOrDefaultAsync(a => a.RoomAllocationId == request.RoomAllocationId && a.userId == CurrentUserId && a.IsActive);
            if (allocation == null) return NotFound(new { message = "Active room allocation not found." });
            var reason = request.Reason?.Trim() ?? "";
            if (reason.Length < 10 || reason.Length > 2000) return Fail("Please enter a reason between 10 and 2000 characters.");
            if (await _db.residenceExitRequests.AnyAsync(r => r.RoomAllocationId == allocation.RoomAllocationId && r.Status == ResidenceExitRequestStatuses.Pending))
                return Fail("There is already a pending exit request for this room.");
            _db.residenceExitRequests.Add(new ResidenceExitRequest
            {
                RoomAllocationId = allocation.RoomAllocationId, userId = CurrentUserId,
                Reason = reason, Status = ResidenceExitRequestStatuses.Pending, DateSubmitted = DateTime.UtcNow
            });
            Notify.To(_db, CurrentUserId, "Your residence exit request has been submitted and is waiting for review.", "info");
            await Notify.ToStaffAsync(_db, $"{User.Identity?.Name ?? "A customer"} has requested to leave {allocation.Room.Accommodation.AccommodationName}, room {allocation.Room.RoomNumber}.", "info");
            await _db.SaveChangesAsync();
            return Ok(new { message = "Your exit request has been submitted for review." });
        }

        /// <summary>Rooms the customer can apply for right now (all of them can be selected - there is no room limit).</summary>
        [HttpGet("applications/available-rooms")]
        public async Task<IActionResult> AvailableRooms(string? roomType)
        {
            var rooms = (await _accommodation.RoomsAsync()).Where(r => r.IsAvailable);
            if (!string.IsNullOrWhiteSpace(roomType)) rooms = rooms.Where(r => r.Room.RoomType == roomType);
            return Ok(rooms.Select(ApiMap.Room));
        }

        [HttpPost("applications")]
        public async Task<IActionResult> Apply([FromBody] ApplicationRequest request)
        {
            if (await IsSuspendedAsync()) return Suspended();
            if (!request.AcceptRules) return Fail("Please confirm that you have read the accommodation rules and conditions.");
            if (request.MoveInDate == null) return Fail("Please choose your planned move-in date.");
            // Profile details always come from the saved profile on the server.
            var (ok, message) = await _accommodation.SubmitApplicationAsync(CurrentUserId, request.RoomIds ?? new List<int>(),
                request.PreferredRoomType, request.NumberOfVehicles, request.MoveInDate.Value, request.Notes);
            return ok ? Ok(new { message }) : Fail(message);
        }

        [HttpDelete("applications/{id:int}")]
        public async Task<IActionResult> Withdraw(int id)
        {
            var app = await _db.applications.FirstOrDefaultAsync(a => a.AccommodationApplicationId == id && a.userId == CurrentUserId);
            if (app == null) return NotFound(new { message = "Application not found." });
            if (app.Status != ApplicationStatuses.Pending) return Fail("Only pending applications can be withdrawn.");
            _db.applications.Remove(app);
            await _db.SaveChangesAsync();
            return Ok(new { message = "Your application was withdrawn." });
        }

        // ------------------------------------------------------------------ billing and payments
        [HttpGet("billing/payable")]
        public async Task<IActionResult> Payable()
        {
            var p = await _billing.GetPayableAsync(CurrentUserId);
            return Ok(new
            {
                outstanding = await _billing.OutstandingAsync(CurrentUserId),
                pendingAcceptance = await _billing.PendingAmountAsync(CurrentUserId),
                warnings = p.Warnings,
                items = p.Items.Select(i => new
                {
                    key = i.Key, type = i.ChargeType, description = i.Description, period = i.Period, amount = i.Amount,
                    isAdvance = i.IsAdvance, note = i.Note, dueDate = i.DueDate, quantity = i.Quantity, quantityUnit = i.QuantityUnit, rate = i.Rate
                })
            });
        }

        /// <summary>Pay one or several charges. The payment is PENDING until a Financial Officer/Agent Head accepts it.</summary>
        [HttpPost("payments/pay")]
        public async Task<IActionResult> Pay([FromBody] PayBillsRequest request)
        {
            if (await IsSuspendedAsync()) return Suspended();
            var outcome = await _payments.PayBillsAsync(CurrentUserId, request.SelectedKeys, request.Card.ToInput());
            return PaymentResult(outcome);
        }

        [HttpPost("payments/electricity")]
        public async Task<IActionResult> BuyElectricity([FromBody] BuyElectricityRequest request)
        {
            if (await IsSuspendedAsync()) return Suspended();
            var outcome = await _payments.BuyElectricityAsync(CurrentUserId, request.Units, request.Card.ToInput());
            if (outcome.Success)
            {
                var token = await _db.electricityTransactions.AsNoTracking().Where(e => e.PaymentId == outcome.Payment!.PaymentId).Select(e => e.MetroToken).FirstOrDefaultAsync();
                return Ok(new { success = true, message = "Electricity purchased.", payment = ApiMap.Payment(outcome.Payment!, token), metroToken = token, metroTokenFormatted = MetroToken.Format(token) });
            }
            return PaymentResult(outcome);
        }

        private IActionResult PaymentResult(PaymentOutcome outcome)
        {
            if (outcome.Success)
                return Ok(new { success = true, message = "Payment submitted. It is pending until a Financial Officer or the Agent Head accepts it.", warnings = outcome.Warnings, payment = ApiMap.Payment(outcome.Payment!) });
            return BadRequest(new { success = false, message = outcome.Errors.FirstOrDefault() ?? "Payment failed.", errors = outcome.Errors, payment = outcome.Payment == null ? null : ApiMap.Payment(outcome.Payment) });
        }

        [HttpGet("payments")]
        public async Task<IActionResult> PaymentHistory(string? status, string? purpose)
        {
            var q = _db.payments.AsNoTracking().Include(p => p.Allocations).ThenInclude(a => a.Charge)
                .Where(p => p.account.userId == CurrentUserId);
            if (!string.IsNullOrWhiteSpace(status)) q = q.Where(p => p.PaymentStatus == status);
            if (!string.IsNullOrWhiteSpace(purpose)) q = q.Where(p => p.PaymentPurpose == purpose);
            var list = await q.OrderByDescending(p => p.DateOfPay).Take(200).ToListAsync();
            return Ok(list.Select(p => ApiMap.Payment(p)));
        }

        [HttpGet("payments/{id:int}")]
        public async Task<IActionResult> PaymentDetails(int id)
        {
            var p = await _db.payments.AsNoTracking().Include(x => x.Allocations).ThenInclude(a => a.Charge)
                .FirstOrDefaultAsync(x => x.PaymentId == id && x.account.userId == CurrentUserId);
            if (p == null) return NotFound(new { message = "Payment not found." });
            var token = await _db.electricityTransactions.AsNoTracking().Where(e => e.PaymentId == id).Select(e => e.MetroToken).FirstOrDefaultAsync();
            return Ok(ApiMap.Payment(p, token));
        }

        [HttpGet("charges")]
        public async Task<IActionResult> Charges()
        {
            await _billing.EnsureChargesAsync(CurrentUserId);
            var accId = await _db.accounts.Where(a => a.userId == CurrentUserId).Select(a => a.AccountId).FirstAsync();
            var list = (await _db.charges.AsNoTracking().Where(c => c.AccountId == accId && c.IssueDate <= DateTime.Today).ToListAsync())
                .OrderByDescending(c => c.BillingPeriod).ThenBy(c => c.ChargeType).Take(100);
            return Ok(list.Select(ApiMap.Charge));
        }

        [HttpGet("electricity")]
        public async Task<IActionResult> Electricity()
        {
            var acc = await _db.accounts.AsNoTracking().FirstAsync(a => a.userId == CurrentUserId);
            var tariff = BillingService.Find(await _billing.LoadTariffsAsync(), TariffCategories.Electricity);
            var recent = await _db.electricityTransactions.AsNoTracking().Where(e => e.AccountId == acc.AccountId)
                .OrderByDescending(e => e.Date).Take(30).ToListAsync();
            return Ok(new
            {
                units = acc.ElectricityUnits,
                rate = tariff == null ? 0m : BillingService.RateOn(tariff, DateTime.Today),
                minUnits = PaymentService.MinElectricityUnits, maxUnits = PaymentService.MaxElectricityUnits,
                transactions = recent.Select(e => new
                {
                    type = e.TransactionType, units = e.Units, rate = e.Rate, amount = e.Amount, balanceAfter = e.BalanceAfter,
                    date = ApiMap.Utc(e.Date), metroToken = e.MetroToken, metroTokenFormatted = MetroToken.Format(e.MetroToken)
                })
            });
        }

        // ------------------------------------------------------------------ maintenance
        [HttpGet("maintenance")]
        public async Task<IActionResult> MaintenanceList()
        {
            var list = await _db.maintenanceRequests.AsNoTracking().Include(m => m.Room).ThenInclude(r => r.Accommodation)
                .Where(m => m.userId == CurrentUserId).OrderByDescending(m => m.DateSubmitted).ToListAsync();
            return Ok(list.Select(ApiMap.Maintenance));
        }

        [HttpGet("maintenance/options")]
        public async Task<IActionResult> MaintenanceOptions()
        {
            var rooms = await _db.roomAllocations.AsNoTracking().Include(a => a.Room).ThenInclude(r => r.Accommodation)
                .Where(a => a.userId == CurrentUserId && a.IsActive).Select(a => a.Room).ToListAsync();
            return Ok(new { rooms = rooms.Select(r => new { roomId = r.RoomId, label = r.Label }), categories = MaintenanceCategories.All });
        }

        /// <summary>Send as multipart/form-data: roomId, category, description and an optional "photo" file.</summary>
        [HttpPost("maintenance")]
        [RequestSizeLimit(8 * 1024 * 1024)]
        public async Task<IActionResult> ReportProblem([FromForm] MaintenanceRequestForm form)
        {
            if (await IsSuspendedAsync()) return Suspended();
            var roomIds = await _db.roomAllocations.Where(a => a.userId == CurrentUserId && a.IsActive).Select(a => a.RoomId).ToListAsync();
            if (roomIds.Count == 0) return Fail("You can report maintenance issues once a room has been allocated to you.", 403);
            if (!roomIds.Contains(form.RoomId)) return Fail("Please choose one of your own rooms.");
            if (string.IsNullOrWhiteSpace(form.Category) || !MaintenanceCategories.All.Contains(form.Category)) return Fail("Please select a valid type of problem.");
            var description = form.Description?.Trim() ?? "";
            if (description.Length < 10 || description.Length > 1500) return Fail("The description must be between 10 and 1500 characters.");

            var (path, error) = await _files.SaveImageAsync(form.Photo, "maintenance");
            if (error != null) return Fail(error);

            _db.maintenanceRequests.Add(new MaintenanceRequest { userId = CurrentUserId, RoomId = form.RoomId, Category = form.Category, Description = description, PhotoPath = path });
            Notify.To(_db, CurrentUserId, $"Your maintenance request ({form.Category}) was submitted. You will be notified when its status changes.", "success");
            await Notify.ToStaffAsync(_db, $"New maintenance request: {form.Category} - {User.Identity?.Name}.", "info");
            await _db.SaveChangesAsync();
            return Ok(new { message = "Maintenance request submitted." });
        }

        // ------------------------------------------------------------------ notifications, appeal, review
        [HttpGet("appeal")]
        public async Task<IActionResult> AppealStatus()
        {
            var acc = await _db.accounts.AsNoTracking().FirstAsync(a => a.userId == CurrentUserId);
            var appeals = await _db.appeals.AsNoTracking().Where(a => a.userId == CurrentUserId).OrderByDescending(a => a.DateSubmitted).ToListAsync();
            return Ok(new
            {
                isSuspended = AccountStatuses.IsSuspended(acc.AccountStatus), reason = acc.SuspensionReason,
                appeals = appeals.Select(a => new { a.AppealId, reason = a.AppealReason, status = a.Status, submitted = ApiMap.Utc(a.DateSubmitted), response = a.ResponseNote })
            });
        }

        [HttpPost("appeal")]
        public async Task<IActionResult> SubmitAppeal([FromBody] AppealRequest request)
        {
            if (!await IsSuspendedAsync()) return Fail("Your account is active - there is nothing to appeal.");
            if (await _db.appeals.AnyAsync(a => a.userId == CurrentUserId && a.Status == AppealStatuses.Pending)) return Fail("You already have an appeal waiting for a decision.");
            var reason = request.Reason?.Trim() ?? "";
            if (reason.Length < 10 || reason.Length > 2000) return Fail("Your appeal must be between 10 and 2000 characters.");
            _db.appeals.Add(new Appeal { userId = CurrentUserId, AppealReason = reason, Status = AppealStatuses.Pending, DateSubmitted = DateTime.UtcNow });
            Notify.To(_db, CurrentUserId, "Your appeal has been submitted. You will be notified of the outcome.", "success");
            await Notify.ToStaffAsync(_db, "A suspended customer has submitted an appeal.", "info");
            await _db.SaveChangesAsync();
            return Ok(new { message = "Appeal submitted." });
        }

        [HttpPost("reviews")]
        public async Task<IActionResult> LeaveReview([FromBody] ReviewRequest request)
        {
            if (request.Rating < 1 || request.Rating > 5) return Fail("Rating must be between 1 and 5.");
            if (string.IsNullOrWhiteSpace(request.Description)) return Fail("Please share your feedback.");
            _db.reviews.Add(new Review { userId = CurrentUserId, RatingScore = request.Rating, ReviewDescription = request.Description.Trim(), DateSubmitted = DateTime.UtcNow });
            await _db.SaveChangesAsync();
            return Ok(new { message = "Thank you for your feedback." });
        }
    }
}
