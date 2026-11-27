using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System.Text.RegularExpressions;
using UFSWebApp.Data;
using UFSWebApp.Models;
using UFSWebApp.Models.ViewModels;

namespace UFSWebApp.Infrastructure
{
    public class NewUserInput
    {
        public string? FName { get; set; }
        public string? Surname { get; set; }
        /// <summary>Student, Staff or FinancialOfficer.</summary>
        public string? PersonType { get; set; }
        public string? IdentificationNumber { get; set; }
        public string? PhoneNumber { get; set; }
        public string? Email { get; set; }
        public string? Password { get; set; }
    }

    public static class CustomerAccounts
    {
        public static async Task<(User? user, List<string> errors)> CreateAsync(
            AppDbContext db, NewUserInput input, bool authorisedByAdmin, string createdByText)
        {
            var errors = new List<string>();
            var type = input.PersonType?.Trim();
            var email = input.Email?.Trim().ToLowerInvariant() ?? "";
            var number = input.IdentificationNumber?.Trim() ?? "";
            var phone = input.PhoneNumber?.Replace(" ", "").Trim() ?? "";
            var isOfficer = type == "FinancialOfficer";

            if (string.IsNullOrWhiteSpace(input.FName)) errors.Add("Please enter your first name.");
            if (string.IsNullOrWhiteSpace(input.Surname)) errors.Add("Please enter your surname.");
            if (type is not ("Student" or "Staff" or "FinancialOfficer")) errors.Add("Please select Student, Staff or Financial Officer.");
            if (string.IsNullOrEmpty(number)) errors.Add("Please enter your student or staff number.");
            else { var e = RegisterViewModel.IdentificationNumberError(type, number); if (e != null) errors.Add(e); }
            var emailError = RegisterViewModel.EmailError(email, number);
            if (emailError != null) errors.Add(emailError);
            if (!Regex.IsMatch(phone, RegisterViewModel.PhonePattern)) errors.Add(RegisterViewModel.PhoneError);
            errors.AddRange(PasswordPolicy.Validate(input.Password, input.FName, input.Surname, email, number));
            if (errors.Count > 0) return (null, errors);

            if (await db.users.AnyAsync(u => u.UserEmail == email))
                return (null, new List<string> { "An account with this email already exists. Please sign in instead." });
            if (await db.users.AnyAsync(u => u.IdentificationNumber == number))
                return (null, new List<string> { "This student/staff number is already registered." });

            var roleName = isOfficer ? "FinancialOfficer" : "Customer";
            var role = await db.roles.FirstOrDefaultAsync(r => r.RoleName == roleName);
            if (role == null) return (null, new List<string> { $"The {roleName} role is not configured." });

            var user = new User
            {
                UserEmail = email,
                UserName = input.FName!.Trim(),
                UserSurname = input.Surname!.Trim(),
                IdentificationNumber = number,
                PhoneNumber = phone,
                PersonType = isOfficer ? "Staff" : type!,
                RoleId = role.RoleId,
                IsAuthorised = !isOfficer || authorisedByAdmin
            };
            user.PassWord = new PasswordHasher<User>().HashPassword(user, input.Password!);
            db.users.Add(user);
            await db.SaveChangesAsync();

            if (!isOfficer)
            {
                db.accounts.Add(new Account
                {
                    userId = user.UserId,
                    AccountNumber = NewAccountNumber(db),
                    AccountStatus = AccountStatuses.Active
                });
                Notify.To(db, user.UserId,
                    $"Welcome to UFS QwaQwa Estate Accommodation, {user.UserName}! {createdByText} Browse accommodation and start an application from your dashboard.", "success");
            }
            else if (!user.IsAuthorised)
            {
                await Notify.ToAdminsAsync(db, $"Financial officer {user.FullName} ({number}) has registered and is waiting for your authorisation.", "warning");
            }
            await db.SaveChangesAsync();
            return (user, errors);
        }

        public static string NewAccountNumber(AppDbContext db)
        {
            while (true)
            {
                var number = "UFS" + Random.Shared.NextInt64(1_000_000_000L, 10_000_000_000L);
                if (!db.accounts.Local.Any(a => a.AccountNumber == number) && !db.accounts.Any(a => a.AccountNumber == number))
                    return number;
            }
        }

        public static async Task DeleteAsync(AppDbContext db, User user)
        {
            var account = await db.accounts.FirstOrDefaultAsync(a => a.userId == user.UserId);
            if (account != null)
            {
                var paymentIds = await db.payments.Where(p => p.AccountId == account.AccountId).Select(p => p.PaymentId).ToListAsync();
                db.paymentAllocations.RemoveRange(await db.paymentAllocations.Where(a => paymentIds.Contains(a.PaymentId)).ToListAsync());
                db.electricityTransactions.RemoveRange(await db.electricityTransactions.Where(e => e.AccountId == account.AccountId).ToListAsync());
                db.waterReadings.RemoveRange(await db.waterReadings.Where(w => w.AccountId == account.AccountId).ToListAsync());
                await db.SaveChangesAsync();
            }
            db.residenceExitRequests.RemoveRange(await db.residenceExitRequests.Where(r => r.userId == user.UserId).ToListAsync());
            await db.SaveChangesAsync();
            db.roomAllocations.RemoveRange(await db.roomAllocations.Where(a => a.userId == user.UserId).ToListAsync());
            await db.SaveChangesAsync();
            db.reports.RemoveRange(await db.reports.Where(r => r.userId == user.UserId).ToListAsync());
            db.notifications.RemoveRange(await db.notifications.Where(n => n.userId == user.UserId).ToListAsync());
            db.users.Remove(user);
            await db.SaveChangesAsync();
        }
    }
}
