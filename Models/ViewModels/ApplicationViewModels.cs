using System.ComponentModel.DataAnnotations;
using UFSWebApp.Infrastructure;

namespace UFSWebApp.Models.ViewModels
{
    public class ApplicationFormViewModel
    {
        // Profile fields: filled by the customer pressing "Load my profile details".
        public string? FullName { get; set; }
        public string? IdentificationNumber { get; set; }
        public string? PhoneNumber { get; set; }
        public string? Email { get; set; }
        public bool ProfileLoaded { get; set; }

        // Accommodation-specific information.
        [Display(Name = "Preferred room type")]
        public string? PreferredRoomType { get; set; }

        [Required(ErrorMessage = "Please select at least one room.")]
        public List<int> RoomIds { get; set; } = new();

        [Range(0, 10, ErrorMessage = "Number of vehicles must be between 0 and 10.")]
        [Display(Name = "Number of vehicles")]
        public int NumberOfVehicles { get; set; }

        [Required(ErrorMessage = "Please choose your planned move-in date."), DataType(DataType.Date)]
        [Display(Name = "Planned move-in date")]
        public DateTime? MoveInDate { get; set; }

        [StringLength(1000)]
        [Display(Name = "Additional notes")]
        public string? Notes { get; set; }

        public bool AcceptRules { get; set; }

        // Display only
        public List<RoomInfo> AvailableRooms { get; set; } = new();
        public string PersonType { get; set; } = "Student";
    }

    public class PayViewModel
    {
        public List<PayableItem> Items { get; set; } = new();
        public List<string> Warnings { get; set; } = new();
        public List<string> SelectedKeys { get; set; } = new();
        public decimal Outstanding { get; set; }
        public decimal ElectricityUnits { get; set; }
        public string AccountNumber { get; set; } = "";
        public string CustomerName { get; set; } = "";
        public string IdentificationNumber { get; set; } = "";
        public string Room { get; set; } = "";

        public string? CardHolder { get; set; }
        public string? CardNumber { get; set; }
        public string? Expiry { get; set; }
        public string? Cvv { get; set; }
    }

    public class ElectricityViewModel
    {
        [Range(1, 1000, ErrorMessage = "Enter between 1 and 1000 units.")]
        [Display(Name = "Units to buy (kWh)")]
        public decimal Units { get; set; } = 50;

        public decimal Rate { get; set; }
        public decimal CurrentUnits { get; set; }
        public string AccountNumber { get; set; } = "";
        public string CustomerName { get; set; } = "";
        public string IdentificationNumber { get; set; } = "";
        public List<ElectricityTransaction> Recent { get; set; } = new();

        public string? CardHolder { get; set; }
        public string? CardNumber { get; set; }
        public string? Expiry { get; set; }
        public string? Cvv { get; set; }
    }

    public class MaintenanceFormViewModel
    {
        [Required(ErrorMessage = "Please select the room the problem is in.")]
        [Display(Name = "Room")]
        public int? RoomId { get; set; }

        [Required(ErrorMessage = "Please select the type of problem.")]
        [Display(Name = "Type of problem")]
        public string Category { get; set; }

        [Required(ErrorMessage = "Please describe the problem."), StringLength(1500, MinimumLength = 10, ErrorMessage = "The description must be between 10 and 1500 characters.")]
        public string Description { get; set; }

        [Display(Name = "Photo (optional)")]
        public IFormFile? Photo { get; set; }

        public List<Room> MyRooms { get; set; } = new();
    }
}
