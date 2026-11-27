using System.ComponentModel.DataAnnotations;

namespace UFSWebApp.Models
{
    public class User
    {
        [Key]
        public int UserId { get; set; }

        [Required(ErrorMessage = "Please enter your student or staff number")]
        public string IdentificationNumber { get; set; }

        [Required]
        public string PersonType { get; set; }

        [Required(ErrorMessage = "Please enter your name")]
        public string UserName { get; set; }

        [Required(ErrorMessage = "Please enter your surname")]
        public string UserSurname { get; set; }

        [Required(ErrorMessage = "Your email is required")]
        [StringLength(100)]
        [DataType(DataType.EmailAddress)]
        public string UserEmail { get; set; }

        [Required(ErrorMessage = "Please enter your phone number")]
        [StringLength(20)]
        public string PhoneNumber { get; set; } = string.Empty;

        [Required(ErrorMessage = "Please enter your password")]
        public string PassWord { get; set; }

        public int FailedLoginAttempts { get; set; } = 0;
        public bool IsLockedOut { get; set; } = false;
        public DateTime? LockoutEndUtc { get; set; }

        /// <summary>Financial officers must be authorised by the administrator before they can sign in.</summary>
        public bool IsAuthorised { get; set; } = true;

        /// <summary>Clearly labelled demonstration account (disabled in production).</summary>
        public bool IsDemo { get; set; }

        /// <summary>Set from the most recent approved accommodation application (drives parking charges).</summary>
        public int NumberOfVehicles { get; set; } = 0;

        public DateTime DateRegistered { get; set; } = DateTime.UtcNow;

        public int RoleId { get; set; }
        public Role role { get; set; }

        public string FullName => $"{UserName} {UserSurname}";

        public List<Report> report { get; set; } = new();
        public List<Review> reviews { get; set; } = new();
        public List<Account> accounts { get; set; } = new();
        public List<Notification> notifications { get; set; } = new();
        public List<Appeal> appeals { get; set; } = new();
        public List<AccommodationApplication> Applications { get; set; } = new();
        public List<RoomAllocation> Allocations { get; set; } = new();
        public List<MaintenanceRequest> MaintenanceRequests { get; set; } = new();
    }
}
