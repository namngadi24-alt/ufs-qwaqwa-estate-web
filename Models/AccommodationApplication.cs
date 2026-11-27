using System.ComponentModel.DataAnnotations;

namespace UFSWebApp.Models
{
    public static class ApplicationStatuses
    {
        public const string Pending = "Pending";
        public const string Approved = "Approved";
        public const string Rejected = "Rejected";
    }

    public class AccommodationApplication
    {
        public int AccommodationApplicationId { get; set; }

        public int userId { get; set; }
        public User user { get; set; }

        [Display(Name = "Preferred room type")]
        public string? PreferredRoomType { get; set; }

        [Range(0, 10, ErrorMessage = "Number of vehicles must be between 0 and 10.")]
        [Display(Name = "Number of vehicles")]
        public int NumberOfVehicles { get; set; }

        [Display(Name = "Planned move-in date")]
        public DateTime PlannedMoveInDate { get; set; }

        [StringLength(1000)]
        public string? Notes { get; set; }

        public string Status { get; set; } = ApplicationStatuses.Pending;
        public DateTime DateSubmitted { get; set; } = DateTime.UtcNow;
        public DateTime? DateDecided { get; set; }
        public string? DecidedBy { get; set; }
        public string? DecisionNote { get; set; }

        public List<ApplicationRoom> Rooms { get; set; } = new();

        public string Reference => $"APP-{AccommodationApplicationId:D5}";
    }

    public class ApplicationRoom
    {
        public int ApplicationRoomId { get; set; }
        public int AccommodationApplicationId { get; set; }
        public AccommodationApplication Application { get; set; }
        public int RoomId { get; set; }
        public Room Room { get; set; }
    }

    public static class ApplicationRules
    {
        public const string RulesText =
            "You may apply for more than one room in a single application, as long as the rooms are available. " +
            "Applications are reviewed by the Financial Officer or the Agent Head. Billing starts at the end of the month in which you move in.";
    }
}
