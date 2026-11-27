using System.ComponentModel.DataAnnotations;

namespace UFSWebApp.Models.ViewModels
{
    public class LoginViewModel
    {
        [Required,EmailAddress, RegularExpression(@"^[^\s@]+@ufs4life\.ac\.za$", ErrorMessage = "Email addresses must end with @ufs4life.ac.za.")]
        [UIHint("email")]
        public string Email { get; set; }

        [Required, DataType(DataType.Password)]
        [UIHint("password")]
        public string Password { get; set; }

        public string? ReturnUrl { get; set; } = "/";
    }

}
