using System.ComponentModel.DataAnnotations;

namespace UFSWebApp.Models
{
    public static class RoomTypes
    {
        public const string Single = "Single room";
        public const string Sharing = "Sharing room (2 students)";
        public const string Bachelor = "Bachelor flat";
        public const string TwoBed = "2-bedroom flat";
        public const string Townhouse = "3-bedroom townhouse";

        public static readonly string[] All = { Single, Sharing, Bachelor, TwoBed, Townhouse };
    }

    public static class AccommodationTypes
    {
        public static readonly string[] All = { "Flats", "Townhouses" };
    }

    /// <summary>A building / complex on the estate (for example a block of flats or a row of townhouses).</summary>
    public class Accommodation
    {
        public int AccommodationId { get; set; }

        [Required, StringLength(100), Display(Name = "Accommodation name")]
        public string AccommodationName { get; set; }

        [Required, StringLength(150), Display(Name = "Location")]
        public string AccommodationLocation { get; set; }

        [Required, Display(Name = "Type")]
        public string AccommodationType { get; set; } = "Flats";

        [Required, StringLength(2000)]
        public string Description { get; set; }

        [StringLength(2000), Display(Name = "Rules and conditions")]
        public string? Rules { get; set; }

        [Display(Name = "Show on the public site")]
        public bool IsPublished { get; set; } = true;

        public ICollection<Room> Rooms { get; set; } = new List<Room>();
        public ICollection<AccommodationPhoto> Photos { get; set; } = new List<AccommodationPhoto>();
    }

    public class AccommodationPhoto
    {
        public int AccommodationPhotoId { get; set; }
        public int AccommodationId { get; set; }
        public Accommodation Accommodation { get; set; }

        [Required]
        public string Path { get; set; }
        [StringLength(150)]
        public string? Caption { get; set; }
    }

    public class Room
    {
        public int RoomId { get; set; }

        public int AccommodationId { get; set; }
        public Accommodation Accommodation { get; set; }

        [Required, StringLength(20), Display(Name = "Room / unit number")]
        public string RoomNumber { get; set; }

        [Required, Display(Name = "Room type")]
        public string RoomType { get; set; }

        [Range(1, 6, ErrorMessage = "Capacity must be between 1 and 6.")]
        [Display(Name = "Capacity (people)")]
        public int Capacity { get; set; } = 1;

        [StringLength(300), Display(Name = "Facilities (comma separated)")]
        public string? Facilities { get; set; }

        [Display(Name = "Under maintenance (not available)")]
        public bool IsUnderMaintenance { get; set; }

        public ICollection<RoomAllocation> Allocations { get; set; } = new List<RoomAllocation>();

        public string Label => $"{Accommodation?.AccommodationName} - Room {RoomNumber}";
    }

    public class RoomAllocation
    {
        public int RoomAllocationId { get; set; }

        public int userId { get; set; }
        public User user { get; set; }

        public int RoomId { get; set; }
        public Room Room { get; set; }

        public int? ApplicationId { get; set; }
        public AccommodationApplication? Application { get; set; }

        public DateTime MoveInDate { get; set; }
        public DateTime? MoveOutDate { get; set; }
        public bool IsActive { get; set; } = true;
    }
}
