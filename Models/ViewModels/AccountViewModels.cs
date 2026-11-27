using System.ComponentModel.DataAnnotations;

namespace UFSWebApp.Models.ViewModels
{
    public class EditUserViewModel
    {
        public int UserId { get; set; }

        [Required(ErrorMessage = "Please enter the first name."), StringLength(50), Display(Name = "First name")]
        public string UserName { get; set; }

        [Required(ErrorMessage = "Please enter the surname."), StringLength(50), Display(Name = "Surname")]
        public string UserSurname { get; set; }

        [Display(Name = "Email address")]
        public string? UserEmail { get; set; }

        [Display(Name = "Student / staff number")]
        public string? IdentificationNumber { get; set; }

        [Required(ErrorMessage = "Please enter the phone number."), Display(Name = "Phone number")]
        [RegularExpression(RegisterViewModel.PhonePattern, ErrorMessage = RegisterViewModel.PhoneError)]
        public string PhoneNumber { get; set; }
    }

    public class ResetPasswordViewModel
    {
        public int UserId { get; set; }
        public string? DisplayName { get; set; }

        [Required(ErrorMessage = "Please enter the new password."), DataType(DataType.Password), Display(Name = "New password")]
        public string NewPassword { get; set; }

        [Required(ErrorMessage = "Please confirm the new password."), DataType(DataType.Password), Display(Name = "Confirm password")]
        [Compare("NewPassword", ErrorMessage = "Passwords do not match.")]
        public string ConfirmPassword { get; set; }
    }

    /// <summary>Financial officer asks for a password reset (must be approved by the administrator).</summary>
    public class ForgotPasswordViewModel
    {
        [Required(ErrorMessage = "Please enter your UFS email address."), EmailAddress]
        public string Email { get; set; }

        [Required(ErrorMessage = "Please enter your staff number."), Display(Name = "Staff number")]
        public string IdentificationNumber { get; set; }

        [Required(ErrorMessage = "Please enter the phone number on your profile."), Display(Name = "Phone number on your profile")]
        public string PhoneNumber { get; set; }

        [Required(ErrorMessage = "Please explain why you need a reset."), StringLength(500, MinimumLength = 5), Display(Name = "Reason for the request")]
        public string Reason { get; set; }
    }

    public class CompleteResetViewModel
    {
        [Required(ErrorMessage = "Please enter your UFS email address."), EmailAddress]
        public string Email { get; set; }

        [Required(ErrorMessage = "Please enter your staff number."), Display(Name = "Staff number")]
        public string IdentificationNumber { get; set; }

        [Required(ErrorMessage = "Please enter a new password."), DataType(DataType.Password), Display(Name = "New password")]
        public string NewPassword { get; set; }

        [Required(ErrorMessage = "Please confirm the new password."), DataType(DataType.Password), Display(Name = "Confirm password")]
        [Compare("NewPassword", ErrorMessage = "Passwords do not match.")]
        public string ConfirmPassword { get; set; }
    }
}
