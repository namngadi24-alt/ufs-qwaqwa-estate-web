using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;

namespace UFSWebApp.Models.ViewModels
{
    public class RegisterViewModel : IValidatableObject
    {
        public const int StudentNumberLength = 10;
        public const int StaffNumberLength = 8;
        public const string PhonePattern = @"^(\+27|0)[0-9]{9}$";
        public const string PhoneError = "Enter a valid South African phone number, e.g. 0821234567 or +27821234567.";

        [Required(ErrorMessage = "Please enter your first name."), StringLength(50), Display(Name = "First name")]
        public string FName { get; set; }

        [Required(ErrorMessage = "Please enter your surname."), StringLength(50)]
        public string Surname { get; set; }

        [Required(ErrorMessage = "Please select how you are registering."), Display(Name = "I am a")]
        public string PersonType { get; set; }

        [Required(ErrorMessage = "Please enter your student or staff number.")]
        [Display(Name = "Student number / Staff number")]
        public string IdentificationNumber { get; set; }

        [Required(ErrorMessage = "Please enter your phone number."), Display(Name = "Phone number")]
        [RegularExpression(PhonePattern, ErrorMessage = PhoneError)]
        public string PhoneNumber { get; set; }

        [Required(ErrorMessage = "Please enter your email address."), EmailAddress]
        public string Email { get; set; }

        [Required(ErrorMessage = "Please enter a password."), DataType(DataType.Password)]
        public string Password { get; set; }

        [Required(ErrorMessage = "Please confirm your password."), DataType(DataType.Password)]
        [Compare("Password", ErrorMessage = "Passwords do not match.")]
        [Display(Name = "Confirm password")]
        public string ConfirmPassword { get; set; }

        public static string? IdentificationNumberError(string? personType, string? number)
        {
            var value = number?.Trim() ?? "";
            if (string.IsNullOrEmpty(value)) return null;
            var length = personType == "Student" ? StudentNumberLength : personType is "Staff" or "FinancialOfficer" ? StaffNumberLength : 0;
            if (length == 0) return null;
            if (Regex.IsMatch(value, $"^[0-9]{{{length}}}$")) return null;
            return personType == "Student"
                ? $"A student number must be exactly {StudentNumberLength} digits (numbers only)."
                : $"A staff number must be exactly {StaffNumberLength} digits (numbers only).";
        }

        public static string? EmailError(string? email, string? number)
        {
            var e = email?.Trim().ToLowerInvariant() ?? "";
            if (!Regex.IsMatch(e, @"^[0-9]+@ufs4life\.ac\.za$"))
                return "Your email must follow the UFS format: StudentOrStaffNumber@ufs4life.ac.za (for example 2024123456@ufs4life.ac.za).";
            if (!string.IsNullOrWhiteSpace(number) && e != $"{number.Trim()}@ufs4life.ac.za")
                return "Your email must be your student/staff number followed by @ufs4life.ac.za.";
            return null;
        }

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            if (PersonType is not ("Student" or "Staff" or "FinancialOfficer"))
                yield return new ValidationResult("Please select Student, Staff or Financial Officer.", new[] { nameof(PersonType) });
            var idError = IdentificationNumberError(PersonType, IdentificationNumber);
            if (idError != null) yield return new ValidationResult(idError, new[] { nameof(IdentificationNumber) });
            var emailError = EmailError(Email, IdentificationNumber);
            if (emailError != null) yield return new ValidationResult(emailError, new[] { nameof(Email) });
        }
    }
}
