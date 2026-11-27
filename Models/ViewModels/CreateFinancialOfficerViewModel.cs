using System.ComponentModel.DataAnnotations;

namespace UFSWebApp.Models.ViewModels
{
    public class CreateFinancialOfficerViewModel : IValidatableObject
    {
        [Required(ErrorMessage = "Please enter the first name."), StringLength(50), Display(Name = "First name")]
        public string FName { get; set; }

        [Required(ErrorMessage = "Please enter the surname."), StringLength(50)]
        public string Surname { get; set; }

        [Required(ErrorMessage = "Please enter the staff number."), Display(Name = "Staff number")]
        public string IdentificationNumber { get; set; }

        [Required(ErrorMessage = "Please enter the phone number."), Display(Name = "Phone number")]
        [RegularExpression(RegisterViewModel.PhonePattern, ErrorMessage = RegisterViewModel.PhoneError)]
        public string PhoneNumber { get; set; }

        [Required(ErrorMessage = "Please enter the email address."), EmailAddress]
        public string Email { get; set; }

        [Required(ErrorMessage = "Please enter a password."), DataType(DataType.Password)]
        public string Password { get; set; }

        [Required(ErrorMessage = "Please confirm the password."), DataType(DataType.Password)]
        [Compare("Password", ErrorMessage = "Passwords do not match.")]
        [Display(Name = "Confirm password")]
        public string ConfirmPassword { get; set; }

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            var idError = RegisterViewModel.IdentificationNumberError("Staff", IdentificationNumber);
            if (idError != null) yield return new ValidationResult(idError, new[] { nameof(IdentificationNumber) });
            var emailError = RegisterViewModel.EmailError(Email, IdentificationNumber);
            if (emailError != null) yield return new ValidationResult(emailError, new[] { nameof(Email) });
        }
    }
}
