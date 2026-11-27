using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using UFSWebApp.Data;
using UFSWebApp.Infrastructure;
using UFSWebApp.Models;

namespace UFSWebApp.Controllers.Api
{
    public class ReportRequest
    {
        public string? ReportTitle { get; set; }
        public string? Findings { get; set; }
        public string? Advice { get; set; }
        public string? Comments { get; set; }
        public string? ReportDescription { get; set; }
        /// <summary>true = submit (locks the report); false = save as draft.</summary>
        public bool Submit { get; set; }
    }
    public class SuspendRequest { public string? Reason { get; set; } }
    public class AccommodationRequest
    {
        public string? AccommodationName { get; set; }
        public string? AccommodationLocation { get; set; }
        public string? AccommodationType { get; set; }
        public string? Description { get; set; }
        public string? Rules { get; set; }
        public bool IsPublished { get; set; } = true;
    }
    public class RoomRequest { public string? RoomNumber { get; set; } public string? RoomType { get; set; } public int Capacity { get; set; } = 1; public string? Facilities { get; set; } }
    public class PhotoForm { public IFormFile? Photo { get; set; } public string? Caption { get; set; } }

    /// <summary>Notification centre - available to every signed-in role (customer, officer, Agent Head).</summary>
    [Route("api/notifications")]
    public class NotificationsApiController : ApiBaseController
    {
        private readonly AppDbContext _db;
        public NotificationsApiController(AppDbContext db) => _db = db;

        [HttpGet]
        public async Task<IActionResult> Index(bool markRead = true)
        {
            var list = await _db.notifications.Where(n => n.userId == CurrentUserId).OrderByDescending(n => n.DateSent).Take(100).ToListAsync();
            var result = list.Select(n => new { id = n.notificationID, message = n.Message, level = n.Level, date = ApiMap.Utc(n.DateSent), isRead = n.IsRead }).ToList();
            if (markRead && list.Any(n => !n.IsRead))
            {
                foreach (var n in list.Where(n => !n.IsRead)) { n.IsRead = true; n.IsToasted = true; }
                await _db.SaveChangesAsync();
            }
            return Ok(result);
        }

        /// <summary>New notifications not yet shown as a pop-up. Poll every 30-60 seconds and show them as toasts.</summary>
        [HttpGet("popups")]
        public async Task<IActionResult> Popups()
        {
            var list = await _db.notifications.Where(n => n.userId == CurrentUserId && !n.IsToasted && !n.IsRead)
                .OrderByDescending(n => n.DateSent).Take(5).ToListAsync();
            foreach (var n in list) n.IsToasted = true;
            if (list.Count > 0) await _db.SaveChangesAsync();
            return Ok(list.Select(n => new { id = n.notificationID, message = n.Message, level = n.Level, date = ApiMap.Utc(n.DateSent) }));
        }

        [HttpGet("unread-count")]
        public async Task<IActionResult> UnreadCount() =>
            Ok(new { count = await _db.notifications.CountAsync(n => n.userId == CurrentUserId && !n.IsRead) });
    }

    /// <summary>Reports, reviews, suspensions/appeals, customer creation and payment details for officers and the Agent Head.</summary>
    [Route("api/staff")]
    [Authorize(Roles = "Admin,FinancialOfficer", AuthenticationSchemes = ApiTokenAuthenticationHandler.SchemeName)]
    public class StaffExtrasApiController : ApiBaseController
    {
        private readonly AppDbContext _db;
        public StaffExtrasApiController(AppDbContext db) => _db = db;

        private string ReviewerText => $"{User.Identity?.Name} ({(User.IsInRole("Admin") ? "Admin" : "Financial Officer")})";

        // ------------------------------------------------------------------ payment details
        [HttpGet("payments/{id:int}")]
        public async Task<IActionResult> PaymentDetails(int id)
        {
            var p = await _db.payments.AsNoTracking().Include(x => x.account).ThenInclude(a => a.user)
                .Include(x => x.Allocations).ThenInclude(a => a.Charge).FirstOrDefaultAsync(x => x.PaymentId == id);
            if (p == null) return NotFound(new { message = "Payment not found." });
            var token = await _db.electricityTransactions.AsNoTracking().Where(e => e.PaymentId == id).Select(e => e.MetroToken).FirstOrDefaultAsync();
            return Ok(new
            {
                payment = ApiMap.Payment(p, token),
                customer = new { userId = p.account.userId, name = p.account.user.FullName, p.account.user.IdentificationNumber, accountNumber = p.account.AccountNumber }
            });
        }

        // ------------------------------------------------------------------ reviews
        [HttpGet("reviews")]
        public async Task<IActionResult> Reviews()
        {
            var list = await _db.reviews.AsNoTracking().Include(r => r.user).OrderByDescending(r => r.DateSubmitted).Take(200).ToListAsync();
            return Ok(list.Select(r => new { r.ReviewId, customer = r.user?.FullName, rating = r.RatingScore, description = r.ReviewDescription, date = ApiMap.Utc(r.DateSubmitted) }));
        }

        // ------------------------------------------------------------------ create customer
        [HttpPost("customers")]
        public async Task<IActionResult> CreateCustomer([FromBody] RegisterRequest request)
        {
            if (request.PersonType != "Student" && request.PersonType != "Staff") return Fail("Customers are students or staff.");
            if (request.Password != request.ConfirmPassword) return Fail("Passwords do not match.");
            var (user, errors) = await CustomerAccounts.CreateAsync(_db, new NewUserInput
            {
                FName = request.FirstName, Surname = request.Surname, PersonType = request.PersonType, IdentificationNumber = request.IdentificationNumber,
                PhoneNumber = request.PhoneNumber, Email = request.Email, Password = request.Password
            }, true, $"Your account was created by {User.Identity?.Name}.");
            return user == null ? BadRequest(new { message = errors.FirstOrDefault() ?? "Could not create the customer.", errors }) : Ok(new { message = $"Customer account created for {user.FullName}.", userId = user.UserId });
        }

        // ------------------------------------------------------------------ suspensions and appeals
        private async Task<(User? user, Account? account)> LoadCustomerAsync(int id)
        {
            var user = await _db.users.Include(u => u.role).FirstOrDefaultAsync(u => u.UserId == id && u.role.RoleName == "Customer");
            var account = user == null ? null : await _db.accounts.FirstOrDefaultAsync(a => a.userId == id);
            return (user, account);
        }

        [HttpPost("customers/{id:int}/suspend")]
        public async Task<IActionResult> Suspend(int id, [FromBody] SuspendRequest request)
        {
            var (user, account) = await LoadCustomerAsync(id);
            if (user == null || account == null) return NotFound(new { message = "Customer not found." });
            if (AccountStatuses.IsSuspended(account.AccountStatus)) return Fail("This account is already suspended.");
            var reason = request.Reason?.Trim() ?? "";
            if (reason.Length < 5 || reason.Length > 1000) return Fail("The reason must be between 5 and 1000 characters.");
            account.AccountStatus = AccountStatuses.Suspended; account.SuspensionReason = reason; account.DateSuspended = DateTime.UtcNow;
            CustomerNotifications.AddSuspendedNotification(_db, user.UserId, reason);
            await _db.SaveChangesAsync();
            return Ok(new { message = $"{user.FullName}'s account has been suspended and they were notified." });
        }

        [HttpPost("customers/{id:int}/reinstate")]
        public async Task<IActionResult> Reinstate(int id)
        {
            var (user, account) = await LoadCustomerAsync(id);
            if (user == null || account == null) return NotFound(new { message = "Customer not found." });
            if (!AccountStatuses.IsSuspended(account.AccountStatus)) return Fail("This account is not suspended.");
            account.AccountStatus = AccountStatuses.Active; account.SuspensionReason = null; account.DateSuspended = null;
            foreach (var appeal in await _db.appeals.Where(a => a.userId == id && a.Status == AppealStatuses.Pending).ToListAsync())
            {
                appeal.Status = AppealStatuses.Approved; appeal.ResponseNote = "Account reinstated."; appeal.ReviewedBy = ReviewerText; appeal.DateReviewed = DateTime.UtcNow;
            }
            CustomerNotifications.AddReinstatedNotification(_db, user.UserId, null);
            await _db.SaveChangesAsync();
            return Ok(new { message = $"{user.FullName}'s account was reinstated and they were notified." });
        }

        [HttpGet("appeals")]
        public async Task<IActionResult> Appeals(string? status = "Pending")
        {
            var q = _db.appeals.AsNoTracking().Include(a => a.user).AsQueryable();
            if (!string.IsNullOrWhiteSpace(status) && status != "All") q = q.Where(a => a.Status == status);
            var list = await q.OrderByDescending(a => a.DateSubmitted).Take(100).ToListAsync();
            return Ok(list.Select(a => new
            {
                appealId = a.AppealId, customer = a.user?.FullName, identificationNumber = a.user?.IdentificationNumber, customerUserId = a.userId,
                reason = a.AppealReason, status = a.Status, submitted = ApiMap.Utc(a.DateSubmitted), responseNote = a.ResponseNote,
                reviewedBy = a.ReviewedBy, dateReviewed = ApiMap.UtcOrNull(a.DateReviewed)
            }));
        }

        [HttpPost("appeals/{id:int}/decision")]
        public async Task<IActionResult> DecideAppeal(int id, [FromBody] DecisionRequest request)
        {
            var appeal = await _db.appeals.FirstOrDefaultAsync(a => a.AppealId == id);
            if (appeal == null) return NotFound(new { message = "Appeal not found." });
            if (appeal.Status != AppealStatuses.Pending) return Fail($"This appeal has already been {appeal.Status.ToLowerInvariant()}.");
            var decision = request.Decision?.Trim().ToLowerInvariant();
            if (decision != "approve" && decision != "reject") return Fail("Decision must be \"approve\" or \"reject\".");
            if (decision == "reject" && string.IsNullOrWhiteSpace(request.Note)) return Fail("Please give the customer a reason when rejecting an appeal.");

            appeal.ResponseNote = request.Note?.Trim(); appeal.ReviewedBy = ReviewerText; appeal.DateReviewed = DateTime.UtcNow;
            if (decision == "approve")
            {
                appeal.Status = AppealStatuses.Approved;
                var account = await _db.accounts.FirstOrDefaultAsync(a => a.userId == appeal.userId);
                if (account != null) { account.AccountStatus = AccountStatuses.Active; account.SuspensionReason = null; account.DateSuspended = null; }
                Notify.To(_db, appeal.userId, "Your appeal was APPROVED.", "success");
                CustomerNotifications.AddReinstatedNotification(_db, appeal.userId, appeal.ResponseNote);
            }
            else
            {
                appeal.Status = AppealStatuses.Rejected;
                Notify.To(_db, appeal.userId, $"Your appeal was not approved and your account remains suspended. Reason: {appeal.ResponseNote} You may submit a new appeal.", "danger");
            }
            await _db.SaveChangesAsync();
            return Ok(new { message = $"Appeal {appeal.Status.ToLowerInvariant()}. The customer was notified." });
        }

        // ------------------------------------------------------------------ reports (officers see their own; the Agent Head sees all)
        private object ReportDto(Report r) => new
        {
            reportId = r.ReportId, title = r.ReportTitle, findings = r.Findings, advice = r.Advice, comments = r.Comments, description = r.ReportDescription,
            status = r.Status, totalCollected = r.TotalCollected, totalPending = r.TotalPending, paymentCount = r.PaymentCount,
            generated = ApiMap.Utc(r.DateGenerated), submitted = ApiMap.UtcOrNull(r.DateSubmitted), author = r.user?.FullName, authorUserId = r.userId
        };

        private async Task<(decimal collected, decimal pending, int count)> SnapshotAsync() =>
        (
            await _db.payments.Where(p => p.PaymentStatus == PaymentStatuses.Successful).SumAsync(p => (decimal?)p.Amount) ?? 0m,
            await _db.charges.SumAsync(c => (decimal?)(c.Amount - c.AmountPaid)) ?? 0m,
            await _db.payments.CountAsync()
        );

        [HttpGet("reports")]
        public async Task<IActionResult> Reports()
        {
            var q = _db.reports.AsNoTracking().Include(r => r.user).AsQueryable();
            if (User.IsInRole("FinancialOfficer")) q = q.Where(r => r.userId == CurrentUserId);
            return Ok((await q.OrderByDescending(r => r.DateGenerated).Take(100).ToListAsync()).Select(ReportDto));
        }

        [HttpGet("reports/snapshot")]
        public async Task<IActionResult> ReportSnapshot()
        {
            var s = await SnapshotAsync();
            return Ok(new { totalCollected = s.collected, totalPending = s.pending, paymentCount = s.count });
        }

        [HttpGet("reports/{id:int}")]
        public async Task<IActionResult> ReportDetails(int id)
        {
            var r = await _db.reports.AsNoTracking().Include(x => x.user).FirstOrDefaultAsync(x => x.ReportId == id);
            if (r == null) return NotFound(new { message = "Report not found." });
            if (User.IsInRole("FinancialOfficer") && r.userId != CurrentUserId) return Fail("You can only view your own reports.", 403);
            return Ok(ReportDto(r));
        }

        private static string? ValidateReport(ReportRequest r)
        {
            if (string.IsNullOrWhiteSpace(r.ReportTitle)) return "Please enter a report title.";
            if (string.IsNullOrWhiteSpace(r.Findings)) return "Please enter your findings.";
            if (string.IsNullOrWhiteSpace(r.Advice)) return "Please enter your advice.";
            return null;
        }

        [HttpPost("reports")]
        public async Task<IActionResult> CreateReport([FromBody] ReportRequest request)
        {
            var error = ValidateReport(request);
            if (error != null) return Fail(error);
            var s = await SnapshotAsync();
            _db.reports.Add(new Report
            {
                ReportTitle = request.ReportTitle!.Trim(), Findings = request.Findings!.Trim(), Advice = request.Advice!.Trim(),
                Comments = request.Comments?.Trim() ?? "", ReportDescription = request.ReportDescription?.Trim() ?? "",
                TotalCollected = s.collected, TotalPending = s.pending, PaymentCount = s.count, userId = CurrentUserId,
                Status = request.Submit ? "Submitted" : "Draft", DateSubmitted = request.Submit ? DateTime.UtcNow : null
            });
            await _db.SaveChangesAsync();
            return Ok(new { message = request.Submit ? "Report submitted." : "Draft saved." });
        }

        [HttpPut("reports/{id:int}")]
        public async Task<IActionResult> EditReport(int id, [FromBody] ReportRequest request)
        {
            var r = await _db.reports.FirstOrDefaultAsync(x => x.ReportId == id);
            if (r == null) return NotFound(new { message = "Report not found." });
            if (r.userId != CurrentUserId) return Fail("You can only edit your own reports.", 403);
            if (r.Status == "Submitted") return Fail("This report has already been submitted and can no longer be edited.");
            var error = ValidateReport(request);
            if (error != null) return Fail(error);
            var s = await SnapshotAsync();
            r.ReportTitle = request.ReportTitle!.Trim(); r.Findings = request.Findings!.Trim(); r.Advice = request.Advice!.Trim();
            r.Comments = request.Comments?.Trim() ?? ""; r.ReportDescription = request.ReportDescription?.Trim() ?? "";
            r.TotalCollected = s.collected; r.TotalPending = s.pending; r.PaymentCount = s.count;
            if (request.Submit) { r.Status = "Submitted"; r.DateSubmitted = DateTime.UtcNow; }
            await _db.SaveChangesAsync();
            return Ok(new { message = request.Submit ? "Report submitted." : "Draft updated." });
        }

        [HttpDelete("reports/{id:int}")]
        public async Task<IActionResult> DeleteReport(int id)
        {
            var r = await _db.reports.FirstOrDefaultAsync(x => x.ReportId == id);
            if (r == null) return NotFound(new { message = "Report not found." });
            if (r.userId != CurrentUserId) return Fail("You can only delete your own drafts.", 403);
            if (r.Status == "Submitted") return Fail("Submitted reports cannot be deleted.");
            _db.reports.Remove(r);
            await _db.SaveChangesAsync();
            return Ok(new { message = "Draft deleted." });
        }

        [Authorize(Roles = "Admin", AuthenticationSchemes = ApiTokenAuthenticationHandler.SchemeName)]
        [HttpPost("reports/summary")]
        public async Task<IActionResult> GenerateSummary()
        {
            var s = await SnapshotAsync();
            _db.reports.Add(new Report
            {
                ReportTitle = $"Monthly Collections Summary - {DateTime.UtcNow:MMMM yyyy}",
                ReportDescription = "Automated system summary generated from the Admin dashboard.",
                Findings = $"Automated system summary: {s.count} payments recorded, R{s.collected:N2} collected, R{s.pending:N2} still outstanding on customer accounts.",
                Advice = "Follow up on outstanding balances and monitor payment compliance.", Comments = "Auto-generated from the Admin dashboard.",
                TotalCollected = s.collected, TotalPending = s.pending, PaymentCount = s.count, userId = CurrentUserId,
                Status = "Submitted", DateSubmitted = DateTime.UtcNow
            });
            await _db.SaveChangesAsync();
            return Ok(new { message = "Summary report generated." });
        }
    }

    public class OfficerEditRequest
    {
        public string? FirstName { get; set; }
        public string? Surname { get; set; }
        public string? IdentificationNumber { get; set; }
        public string? PhoneNumber { get; set; }
        public string? Email { get; set; }
    }

    /// <summary>Agent Head: accommodation, rooms and photographs (same as the website's Manage accommodation).</summary>
    [Route("api/admin/accommodations")]
    [Authorize(Roles = "Admin", AuthenticationSchemes = ApiTokenAuthenticationHandler.SchemeName)]
    public class AccommodationAdminApiController : ApiBaseController
    {
        private readonly AppDbContext _db;
        private readonly CatalogService _catalog;
        private readonly AccommodationService _rooms;
        private readonly FileStorage _files;
        public AccommodationAdminApiController(AppDbContext db, CatalogService catalog, AccommodationService rooms, FileStorage files)
        { _db = db; _catalog = catalog; _rooms = rooms; _files = files; }

        [HttpGet]
        public async Task<IActionResult> List()
        {
            var list = await _db.accommodations.AsNoTracking().Include(a => a.Photos).OrderBy(a => a.AccommodationName).ToListAsync();
            var rooms = await _rooms.RoomsAsync();
            return Ok(list.Select(a =>
            {
                var mine = rooms.Where(r => r.Room.AccommodationId == a.AccommodationId).ToList();
                return new
                {
                    accommodationId = a.AccommodationId, name = a.AccommodationName, location = a.AccommodationLocation, type = a.AccommodationType,
                    description = a.Description, rules = a.Rules, isPublished = a.IsPublished,
                    photos = a.Photos.Select(p => new { photoId = p.AccommodationPhotoId, url = p.Path, caption = p.Caption }),
                    roomsTotal = mine.Count, roomsAvailable = mine.Count(r => r.IsAvailable)
                };
            }));
        }

        private string? Validate(AccommodationRequest r)
        {
            if (string.IsNullOrWhiteSpace(r.AccommodationName) || r.AccommodationName.Length > 100) return "Enter the accommodation name (max 100 characters).";
            if (string.IsNullOrWhiteSpace(r.AccommodationLocation) || r.AccommodationLocation.Length > 150) return "Enter the location (max 150 characters).";
            if (r.AccommodationType == null || !AccommodationTypes.All.Contains(r.AccommodationType)) return "Please choose a valid type.";
            if (string.IsNullOrWhiteSpace(r.Description) || r.Description.Length > 2000) return "Enter a description (max 2000 characters).";
            if (r.Rules?.Length > 2000) return "Rules must be at most 2000 characters.";
            return null;
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] AccommodationRequest request)
        {
            var error = Validate(request);
            if (error != null) return Fail(error);
            var a = new Accommodation
            {
                AccommodationName = request.AccommodationName!.Trim(), AccommodationLocation = request.AccommodationLocation!.Trim(),
                AccommodationType = request.AccommodationType!, Description = request.Description!.Trim(), Rules = request.Rules?.Trim(), IsPublished = request.IsPublished
            };
            _db.accommodations.Add(a);
            await _db.SaveChangesAsync();
            _catalog.Invalidate();
            return Ok(new { message = "Accommodation saved.", accommodationId = a.AccommodationId });
        }

        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(int id, [FromBody] AccommodationRequest request)
        {
            var a = await _db.accommodations.FirstOrDefaultAsync(x => x.AccommodationId == id);
            if (a == null) return NotFound(new { message = "Accommodation not found." });
            var error = Validate(request);
            if (error != null) return Fail(error);
            a.AccommodationName = request.AccommodationName!.Trim(); a.AccommodationLocation = request.AccommodationLocation!.Trim();
            a.AccommodationType = request.AccommodationType!; a.Description = request.Description!.Trim(); a.Rules = request.Rules?.Trim(); a.IsPublished = request.IsPublished;
            await _db.SaveChangesAsync();
            _catalog.Invalidate();
            return Ok(new { message = "Accommodation saved." });
        }

        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Delete(int id)
        {
            var a = await _db.accommodations.FirstOrDefaultAsync(x => x.AccommodationId == id);
            if (a == null) return NotFound(new { message = "Accommodation not found." });
            var history = await _db.roomAllocations.AnyAsync(x => x.Room.AccommodationId == id) ||
                          await _db.applicationRooms.AnyAsync(x => x.Room.AccommodationId == id) ||
                          await _db.maintenanceRequests.AnyAsync(x => x.Room.AccommodationId == id);
            if (history) return Fail("This accommodation has allocation, application or maintenance history, so it cannot be deleted. Hide it from the public site instead.");
            _db.accommodations.Remove(a);
            await _db.SaveChangesAsync();
            _catalog.Invalidate();
            return Ok(new { message = "Accommodation deleted." });
        }

        [HttpGet("{id:int}/rooms")]
        public async Task<IActionResult> Rooms(int id)
        {
            _catalog.Invalidate();
            return Ok((await _rooms.RoomsAsync(id)).Select(ApiMap.Room));
        }

        [HttpPost("{id:int}/rooms")]
        public async Task<IActionResult> AddRoom(int id, [FromBody] RoomRequest request)
        {
            var number = request.RoomNumber?.Trim() ?? "";
            if (!await _db.accommodations.AnyAsync(a => a.AccommodationId == id)) return NotFound(new { message = "Accommodation not found." });
            if (number.Length == 0 || number.Length > 20) return Fail("Enter a room number (up to 20 characters).");
            if (request.RoomType == null || !RoomTypes.All.Contains(request.RoomType)) return Fail("Please choose a valid room type.");
            if (request.Capacity < 1 || request.Capacity > 6) return Fail("Capacity must be between 1 and 6.");
            if (await _db.rooms.AnyAsync(r => r.AccommodationId == id && r.RoomNumber == number)) return Fail($"Room {number} already exists here.");
            _db.rooms.Add(new Room { AccommodationId = id, RoomNumber = number, RoomType = request.RoomType, Capacity = request.Capacity, Facilities = request.Facilities?.Trim() });
            await _db.SaveChangesAsync();
            _catalog.Invalidate();
            return Ok(new { message = $"Room {number} added." });
        }

        [HttpPost("rooms/{roomId:int}/toggle-maintenance")]
        public async Task<IActionResult> ToggleMaintenance(int roomId)
        {
            var room = await _db.rooms.FirstOrDefaultAsync(r => r.RoomId == roomId);
            if (room == null) return NotFound(new { message = "Room not found." });
            room.IsUnderMaintenance = !room.IsUnderMaintenance;
            await _db.SaveChangesAsync();
            _catalog.Invalidate();
            return Ok(new { message = $"Room {room.RoomNumber} is now {(room.IsUnderMaintenance ? "marked under maintenance" : "available again")}.", isUnderMaintenance = room.IsUnderMaintenance });
        }

        [HttpDelete("rooms/{roomId:int}")]
        public async Task<IActionResult> DeleteRoom(int roomId)
        {
            var room = await _db.rooms.FirstOrDefaultAsync(r => r.RoomId == roomId);
            if (room == null) return NotFound(new { message = "Room not found." });
            if (await _db.roomAllocations.AnyAsync(a => a.RoomId == roomId) || await _db.applicationRooms.AnyAsync(a => a.RoomId == roomId) ||
                await _db.maintenanceRequests.AnyAsync(m => m.RoomId == roomId) || await _db.charges.AnyAsync(c => c.RoomId == roomId))
                return Fail("This room has history and cannot be deleted. Mark it as under maintenance instead.");
            _db.rooms.Remove(room);
            await _db.SaveChangesAsync();
            _catalog.Invalidate();
            return Ok(new { message = "Room deleted." });
        }

        /// <summary>multipart/form-data: "photo" file and optional "caption".</summary>
        [HttpPost("{id:int}/photos")]
        [RequestSizeLimit(8 * 1024 * 1024)]
        public async Task<IActionResult> UploadPhoto(int id, [FromForm] PhotoForm form)
        {
            if (!await _db.accommodations.AnyAsync(a => a.AccommodationId == id)) return NotFound(new { message = "Accommodation not found." });
            if (form.Photo == null) return Fail("Please choose a photo to upload.");
            var (path, error) = await _files.SaveImageAsync(form.Photo, "accommodation");
            if (error != null) return Fail(error);
            _db.accommodationPhotos.Add(new AccommodationPhoto { AccommodationId = id, Path = path!, Caption = form.Caption?.Trim() });
            await _db.SaveChangesAsync();
            _catalog.Invalidate();
            return Ok(new { message = "Photo added." });
        }

        [HttpDelete("photos/{photoId:int}")]
        public async Task<IActionResult> DeletePhoto(int photoId)
        {
            var p = await _db.accommodationPhotos.FirstOrDefaultAsync(x => x.AccommodationPhotoId == photoId);
            if (p == null) return NotFound(new { message = "Photo not found." });
            _db.accommodationPhotos.Remove(p);
            await _db.SaveChangesAsync();
            _catalog.Invalidate();
            return Ok(new { message = "Photo removed." });
        }
    }
}
