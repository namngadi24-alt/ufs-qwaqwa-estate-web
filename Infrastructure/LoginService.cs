using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using UFSWebApp.Data;
using UFSWebApp.Models;

namespace UFSWebApp.Infrastructure
{
    public enum LoginOutcome { Success, Invalid, Locked, Revoked }

    public record LoginResult(LoginOutcome Outcome, User? User, string? Message)
    {
        public bool Succeeded => Outcome == LoginOutcome.Success;
    }

    public class LoginService
    {
        public const int MaxFailedAttempts = 3;
        private static readonly TimeSpan AdminLockoutDuration = TimeSpan.FromMinutes(15);

        private readonly AppDbContext _db;
        private readonly PasswordHasher<User> _hasher = new();

        public LoginService(AppDbContext db) => _db = db;

        public async Task<LoginResult> CheckAsync(string? emailInput, string? password)
        {
            var email = emailInput?.Trim().ToLowerInvariant() ?? "";
            if (!UfsEmailValidation.IsUfsEmail(email) || string.IsNullOrEmpty(password))
                return new LoginResult(LoginOutcome.Invalid, null, "Password or email is incorrect.");

            var user = await _db.users.Include(u => u.role).FirstOrDefaultAsync(u => u.UserEmail == email);
            if (user == null)
                return new LoginResult(LoginOutcome.Invalid, null, "Password or email is incorrect.");

            var roleName = user.role?.RoleName ?? "Customer";
            var isAdmin = roleName == "Admin";

            if (user.IsLockedOut && isAdmin && user.LockoutEndUtc <= DateTime.UtcNow)
            {
                user.IsLockedOut = false;
                user.FailedLoginAttempts = 0;
                user.LockoutEndUtc = null;
                await _db.SaveChangesAsync();
            }

            if (user.IsLockedOut)
                return new LoginResult(LoginOutcome.Locked, user, LockedMessage(user, roleName, justLocked: false));

            if (_hasher.VerifyHashedPassword(user, user.PassWord, password) == PasswordVerificationResult.Failed)
            {
                user.FailedLoginAttempts++;
                if (user.FailedLoginAttempts >= MaxFailedAttempts)
                {
                    user.IsLockedOut = true;
                    if (isAdmin) user.LockoutEndUtc = DateTime.UtcNow.Add(AdminLockoutDuration);
                    await _db.SaveChangesAsync();
                    return new LoginResult(LoginOutcome.Locked, user, LockedMessage(user, roleName, justLocked: true));
                }
                await _db.SaveChangesAsync();
                return new LoginResult(LoginOutcome.Invalid, null, "Password or email is incorrect.");
            }

            if (roleName == "FinancialOfficer" && !user.IsAuthorised)
                return new LoginResult(LoginOutcome.Revoked, user,
                    "Your financial officer account is not authorised. Please wait for (or ask) the administrator to authorise it.");

            user.FailedLoginAttempts = 0;
            user.IsLockedOut = false;
            user.LockoutEndUtc = null;
            await _db.SaveChangesAsync();
            return new LoginResult(LoginOutcome.Success, user, null);
        }

        private static string LockedMessage(User user, string roleName, bool justLocked)
        {
            var start = justLocked
                ? $"Your account has been locked after {MaxFailedAttempts} incorrect password attempts."
                : $"This account is locked after {MaxFailedAttempts} incorrect password attempts.";

            if (roleName == "Admin")
            {
                var minutes = Math.Max(1, (int)Math.Ceiling(((user.LockoutEndUtc ?? DateTime.UtcNow) - DateTime.UtcNow).TotalMinutes));
                return $"{start} Please try again in {minutes} minute(s).";
            }

            var unlocker = roleName == "Customer" ? "a financial officer or the administrator" : "the administrator";
            return $"{start} Contact {unlocker} to reset your password and unlock the account.";
        }
    }
}
