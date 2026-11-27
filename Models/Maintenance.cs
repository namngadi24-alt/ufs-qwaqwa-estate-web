using System.ComponentModel.DataAnnotations;

namespace UFSWebApp.Models
{
    public static class MaintenanceStatuses
    {
        public const string Submitted = "Submitted";
        public const string InProgress = "In progress";
        public const string Resolved = "Resolved";
        public const string Closed = "Closed";
        public static readonly string[] All = { Submitted, InProgress, Resolved, Closed };
    }

    public static class MaintenanceCategories
    {
        public static readonly string[] All =
        {
            "Plumbing / leak", "Electrical", "Furniture / fittings", "Appliance", "Doors / windows / locks",
            "Internet / Wi-Fi", "Pest control", "Structural / damp", "Other"
        };
    }

    public class MaintenanceRequest
    {
        public int MaintenanceRequestId { get; set; }

        public int userId { get; set; }
        public User user { get; set; }

        public int RoomId { get; set; }
        public Room Room { get; set; }

        [Required]
        public string Category { get; set; }

        [Required, StringLength(1500, MinimumLength = 10)]
        public string Description { get; set; }

        public string? PhotoPath { get; set; }
        public string Status { get; set; } = MaintenanceStatuses.Submitted;
        public DateTime DateSubmitted { get; set; } = DateTime.UtcNow;
        public DateTime DateUpdated { get; set; } = DateTime.UtcNow;
        public string? StaffNote { get; set; }
        public string? UpdatedBy { get; set; }
    }

    public static class ResetRequestStatuses
    {
        public const string Pending = "Pending";
        public const string Approved = "Approved";
        public const string Rejected = "Rejected";
        public const string Completed = "Completed";
        public const string Expired = "Expired";
    }

    public class PasswordResetRequest
    {
        public int PasswordResetRequestId { get; set; }
        public int userId { get; set; }
        public User user { get; set; }
        public string Reason { get; set; }
        public string Status { get; set; } = ResetRequestStatuses.Pending;
        public DateTime DateRequested { get; set; } = DateTime.UtcNow;
        public string? ReviewedBy { get; set; }
        public DateTime? DateReviewed { get; set; }
        public string? AdminNote { get; set; }
        public DateTime? ExpiresUtc { get; set; }
        public DateTime? DateCompleted { get; set; }
    }
}
