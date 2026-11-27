using System.ComponentModel.DataAnnotations;

namespace UFSWebApp.Models
{
    public class Appeal
    {
        public int AppealId { get; set; }

        [Required(ErrorMessage = "Please explain why your account should be reinstated.")]
        [StringLength(2000, MinimumLength = 10, ErrorMessage = "Your appeal must be between 10 and 2000 characters.")]
        [Display(Name = "Reason for your appeal")]
        public string AppealReason { get; set; }

        public string Status { get; set; } = AppealStatuses.Pending;

        public DateTime DateSubmitted { get; set; } = DateTime.UtcNow;

        public string? ResponseNote { get; set; }
        public string? ReviewedBy { get; set; }
        public DateTime? DateReviewed { get; set; }

        public int userId { get; set; }
        public User? user { get; set; }
    }

    public static class AppealStatuses
    {
        public const string Pending = "Pending";
        public const string Approved = "Approved";
        public const string Rejected = "Rejected";
    }
}
