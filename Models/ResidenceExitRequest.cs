using System.ComponentModel.DataAnnotations;

namespace UFSWebApp.Models;

public class ResidenceExitRequest
{
    public int ResidenceExitRequestId { get; set; }
    public int RoomAllocationId { get; set; }
    public RoomAllocation RoomAllocation { get; set; } = null!;
    public int userId { get; set; }
    public User user { get; set; } = null!;

    [Required, StringLength(2000, MinimumLength = 10, ErrorMessage = "Please provide a reason between 10 and 2000 characters.")]
    [Display(Name = "Reason for leaving")]
    public string Reason { get; set; } = "";
    [StringLength(300)] public string? DecisionNote { get; set; }
    [StringLength(150)] public string? DecidedBy { get; set; }
    public string Status { get; set; } = ResidenceExitRequestStatuses.Pending;
    public DateTime DateSubmitted { get; set; } = DateTime.UtcNow;
    public DateTime? DateDecided { get; set; }
}

public static class ResidenceExitRequestStatuses
{
    public const string Pending = "Pending";
    public const string Approved = "Approved";
    public const string Rejected = "Rejected";
}
