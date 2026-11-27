using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using UFSWebApp.Data;
using UFSWebApp.Infrastructure;
using UFSWebApp.Models;

namespace UFSWebApp.Controllers.Api
{
    public class LoginRequest { public string? Email { get; set; } public string? Password { get; set; } }

    public class RegisterRequest
    {
        public string? FirstName { get; set; }
        public string? Surname { get; set; }
        /// <summary>"Student" or "Staff".</summary>
        public string? PersonType { get; set; }
        public string? IdentificationNumber { get; set; }
        public string? PhoneNumber { get; set; }
        public string? Email { get; set; }
        public string? Password { get; set; }
        public string? ConfirmPassword { get; set; }
    }

    public class ResetRequestDto
    {
        public string? Email { get; set; }
        public string? IdentificationNumber { get; set; }
        public string? PhoneNumber { get; set; }
        public string? Reason { get; set; }
    }

    public class CompleteResetDto
    {
        public string? Email { get; set; }
        public string? IdentificationNumber { get; set; }
        public string? NewPassword { get; set; }
        public string? ConfirmPassword { get; set; }
    }

    /// <summary>Endpoints that do not need a token: login, register, browsing accommodation and public rates.</summary>
    [ApiController]
    [AllowAnonymous]
    [Route("api")]
    public class PublicApiController : ControllerBase
    {
        private readonly AppDbContext _db;
        private readonly LoginService _login;
        private readonly ApiTokenService _tokens;
        private readonly CatalogService _catalog;
        private readonly AccommodationService _rooms;

        public PublicApiController(AppDbContext db, LoginService login, ApiTokenService tokens, CatalogService catalog, AccommodationService rooms)
        {
            _db = db; _login = login; _tokens = tokens; _catalog = catalog; _rooms = rooms;
        }

        [HttpGet("ping")]
        public IActionResult Ping() => Ok(new { status = "ok", time = DateTime.UtcNow });

        // ------------------------------------------------------------------ authentication
        [HttpPost("auth/login")]
        public async Task<IActionResult> Login([FromBody] LoginRequest request)
        {
            var result = await _login.CheckAsync(request.Email, request.Password);
            if (!result.Succeeded)
            {
                var status = result.Outcome switch { LoginOutcome.Locked => 423, LoginOutcome.Revoked => 403, _ => 401 };
                return StatusCode(status, new { message = result.Message });
            }
            var token = await _tokens.IssueAsync(result.User!.UserId);
            return Ok(new
            {
                token,
                tokenType = "Bearer",
                expiresUtc = DateTime.UtcNow.Add(ApiTokenService.Lifetime),
                user = ApiMap.User(result.User)
            });
        }

        [HttpPost("auth/register")]
        public async Task<IActionResult> Register([FromBody] RegisterRequest request)
        {
            if (request.PersonType != "Student" && request.PersonType != "Staff")
                return BadRequest(new { message = "Please choose Student or Staff.", errors = new[] { "Please choose Student or Staff." } });
            if (request.Password != request.ConfirmPassword)
                return BadRequest(new { message = "Passwords do not match.", errors = new[] { "Passwords do not match." } });

            var (user, errors) = await CustomerAccounts.CreateAsync(_db, new NewUserInput
            {
                FName = request.FirstName, Surname = request.Surname, PersonType = request.PersonType,
                IdentificationNumber = request.IdentificationNumber, PhoneNumber = request.PhoneNumber,
                Email = request.Email, Password = request.Password
            }, false, "Your account and personal profile have been created.");
            if (user == null) return BadRequest(new { message = errors.FirstOrDefault() ?? "Registration failed.", errors });
            return Ok(new { message = "Registration successful. Please sign in.", userId = user.UserId });
        }

        // Financial officer password reset: request -> administrator approves -> officer completes.
        [HttpPost("auth/password-reset/request")]
        public async Task<IActionResult> RequestReset([FromBody] ResetRequestDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.Reason) || dto.Reason.Trim().Length < 5)
                return BadRequest(new { message = "Please explain why you need a reset (at least 5 characters)." });
            var email = dto.Email?.Trim().ToLowerInvariant() ?? "";
            var number = dto.IdentificationNumber?.Trim() ?? "";
            var phone = dto.PhoneNumber?.Replace(" ", "").Trim() ?? "";
            var officer = await _db.users.Include(u => u.role).FirstOrDefaultAsync(u =>
                u.UserEmail == email && u.IdentificationNumber == number && u.PhoneNumber == phone && u.role.RoleName == "FinancialOfficer");
            if (officer != null && !await _db.passwordResetRequests.AnyAsync(r => r.userId == officer.UserId && r.Status == ResetRequestStatuses.Pending))
            {
                _db.passwordResetRequests.Add(new PasswordResetRequest { userId = officer.UserId, Reason = dto.Reason.Trim() });
                await Notify.ToAdminsAsync(_db, $"Password reset request from Financial Officer {officer.FullName}. Review it under Reset requests.", "warning");
                await _db.SaveChangesAsync();
            }
            return Ok(new { message = "If the details match a Financial Officer account, your request was sent to the administrator." });
        }

        [HttpPost("auth/password-reset/complete")]
        public async Task<IActionResult> CompleteReset([FromBody] CompleteResetDto dto)
        {
            if (dto.NewPassword != dto.ConfirmPassword) return BadRequest(new { message = "Passwords do not match." });
            var email = dto.Email?.Trim().ToLowerInvariant() ?? "";
            var number = dto.IdentificationNumber?.Trim() ?? "";
            var officer = await _db.users.Include(u => u.role).FirstOrDefaultAsync(u =>
                u.UserEmail == email && u.IdentificationNumber == number && u.role.RoleName == "FinancialOfficer");
            var request = officer == null ? null : await _db.passwordResetRequests
                .Where(r => r.userId == officer.UserId && r.Status == ResetRequestStatuses.Approved)
                .OrderByDescending(r => r.DateReviewed).FirstOrDefaultAsync();
            if (officer == null || request == null) return BadRequest(new { message = "No approved reset request was found for these details." });
            if (request.ExpiresUtc < DateTime.UtcNow)
            {
                request.Status = ResetRequestStatuses.Expired;
                await _db.SaveChangesAsync();
                return BadRequest(new { message = "Your approved request has expired. Please submit a new request." });
            }
            var errors = PasswordPolicy.Validate(dto.NewPassword, officer.UserName, officer.UserSurname, officer.UserEmail, officer.IdentificationNumber).ToList();
            if (errors.Count > 0) return BadRequest(new { message = errors[0], errors });

            var hasher = new PasswordHasher<User>();
            if (hasher.VerifyHashedPassword(officer, officer.PassWord, dto.NewPassword!) != PasswordVerificationResult.Failed)
                return BadRequest(new { message = "Your new password must be different from your old password." });
            officer.PassWord = hasher.HashPassword(officer, dto.NewPassword!);
            officer.FailedLoginAttempts = 0; officer.IsLockedOut = false; officer.LockoutEndUtc = null;
            request.Status = ResetRequestStatuses.Completed; request.DateCompleted = DateTime.UtcNow;
            Notify.To(_db, officer.UserId, "Your password was changed successfully.", "success");
            await _db.SaveChangesAsync();
            return Ok(new { message = "Your password has been changed. You can now sign in." });
        }

        // ------------------------------------------------------------------ public accommodation browsing
        [HttpGet("accommodations")]
        public async Task<IActionResult> Accommodations(string? type, string? roomType, bool availableOnly = false)
        {
            var list = (await _catalog.GetAccommodationsAsync()).Where(a => a.IsPublished).ToList();
            if (!string.IsNullOrWhiteSpace(type)) list = list.Where(a => a.AccommodationType == type).ToList();

            var rooms = await _rooms.RoomsAsync();
            var filtered = rooms.AsEnumerable();
            if (!string.IsNullOrWhiteSpace(roomType)) filtered = filtered.Where(r => r.Room.RoomType == roomType);
            if (availableOnly) filtered = filtered.Where(r => r.IsAvailable);
            if (!string.IsNullOrWhiteSpace(roomType) || availableOnly)
            {
                var ids = filtered.Select(r => r.Room.AccommodationId).ToHashSet();
                list = list.Where(a => ids.Contains(a.AccommodationId)).ToList();
            }

            return Ok(list.Select(a =>
            {
                var mine = rooms.Where(r => r.Room.AccommodationId == a.AccommodationId).ToList();
                var prices = mine.Where(r => r.Rent != null).Select(r => r.Rent!.Value).ToList();
                return new
                {
                    accommodationId = a.AccommodationId, name = a.AccommodationName, location = a.AccommodationLocation, type = a.AccommodationType,
                    description = a.Description, rules = a.Rules,
                    photos = a.Photos.Select(p => new { url = p.Path, caption = p.Caption }),
                    roomsTotal = mine.Count, roomsAvailable = mine.Count(r => r.IsAvailable),
                    fromPrice = prices.Count > 0 ? prices.Min() : (decimal?)null
                };
            }));
        }

        [HttpGet("accommodations/{id:int}")]
        public async Task<IActionResult> AccommodationDetails(int id)
        {
            var a = (await _catalog.GetAccommodationsAsync()).FirstOrDefault(x => x.AccommodationId == id && x.IsPublished);
            if (a == null) return NotFound(new { message = "Accommodation not found." });
            var rooms = await _rooms.RoomsAsync(id);
            var tariffs = await _catalog.GetTariffsAsync();
            return Ok(new
            {
                accommodationId = a.AccommodationId, name = a.AccommodationName, location = a.AccommodationLocation, type = a.AccommodationType,
                description = a.Description, rules = a.Rules, applicationRules = ApplicationRules.RulesText,
                photos = a.Photos.Select(p => new { url = p.Path, caption = p.Caption }),
                rooms = rooms.Select(ApiMap.Room),
                rates = tariffs.Where(t => t.Category != TariffCategories.Rent).Select(ApiMap.Tariff)
            });
        }

        [HttpGet("tariffs")]
        public async Task<IActionResult> Tariffs() =>
            Ok((await _catalog.GetTariffsAsync()).OrderBy(t => t.Category).ThenBy(t => t.Name).Select(ApiMap.Tariff));

        [HttpGet("meta")]
        public IActionResult Meta() => Ok(new
        {
            roomTypes = RoomTypes.All, accommodationTypes = AccommodationTypes.All,
            maintenanceCategories = MaintenanceCategories.All, maintenanceStatuses = MaintenanceStatuses.All,
            personTypes = new[] { "Student", "Staff" }
        });
    }

    /// <summary>Authenticated account endpoints shared by every role.</summary>
    [Route("api/auth")]
    public class AuthApiController : ApiBaseController
    {
        private readonly AppDbContext _db;
        private readonly ApiTokenService _tokens;
        public AuthApiController(AppDbContext db, ApiTokenService tokens) { _db = db; _tokens = tokens; }

        [HttpGet("me")]
        public async Task<IActionResult> Me()
        {
            var u = await _db.users.AsNoTracking().Include(x => x.role).FirstAsync(x => x.UserId == CurrentUserId);
            return Ok(ApiMap.User(u));
        }

        [HttpPost("logout")]
        public async Task<IActionResult> Logout()
        {
            var token = ApiTokenAuthenticationHandler.ReadToken(Request);
            if (token != null) await _tokens.RevokeAsync(token);
            return Ok(new { message = "Signed out." });
        }
    }
}
