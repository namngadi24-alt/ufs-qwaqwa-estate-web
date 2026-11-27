using System.Text.RegularExpressions;

namespace UFSWebApp.Infrastructure
{
    public static class UfsEmailValidation
    {
        private static readonly Regex Pattern = new(
            @"^[^\s@]+@ufs4life\.ac\.za$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public static bool IsUfsEmail(string? email) =>
            !string.IsNullOrWhiteSpace(email) && Pattern.IsMatch(email.Trim());

        public const string ErrorMessage = "Email addresses must end with @ufs4life.ac.za.";
    }
}
