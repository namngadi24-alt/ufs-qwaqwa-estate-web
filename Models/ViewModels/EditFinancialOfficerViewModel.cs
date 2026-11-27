using System.ComponentModel.DataAnnotations;

namespace UFSWebApp.Models.ViewModels;

public class EditFinancialOfficerViewModel : IValidatableObject
{
    public int UserId { get; set; }

    [Required, StringLength(50), Display(Name = "First name")]
    public string FirstName { get; set; } = "";

    [Required, StringLength(50)]
    public string Surname { get; set; } = "";

    [Required, Display(Name = "Staff number")]
    public string IdentificationNumber { get; set; } = "";

    [Required, RegularExpression(RegisterViewModel.PhonePattern, ErrorMessage = RegisterViewModel.PhoneError), Display(Name = "Phone number")]
    public string PhoneNumber { get; set; } = "";

    [Required, EmailAddress, Display(Name = "Email address")]
    public string Email { get; set; } = "";

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        var idError = RegisterViewModel.IdentificationNumberError("FinancialOfficer", IdentificationNumber);
        if (idError != null) yield return new ValidationResult(idError, new[] { nameof(IdentificationNumber) });
        var emailError = RegisterViewModel.EmailError(Email, IdentificationNumber);
        if (emailError != null) yield return new ValidationResult(emailError, new[] { nameof(Email) });
    }
}
