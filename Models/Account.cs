namespace UFSWebApp.Models
{
    public class Account
    {
        public int AccountId { get; set; }
        public string AccountNumber { get; set; }
        public string AccountStatus { get; set; }
        public DateTime DateCreated { get; set; } = DateTime.UtcNow;
        public string? SuspensionReason { get; set; }
        public DateTime? DateSuspended { get; set; }

        /// <summary>Prepaid electricity units (kWh) currently available.</summary>
        public decimal ElectricityUnits { get; set; }

        public int userId { get; set; }
        public User user { get; set; }
        public ICollection<Payment> payments { get; set; } = new List<Payment>();
        public ICollection<Charge> Charges { get; set; } = new List<Charge>();
    }

    public static class AccountStatuses
    {
        public const string Active = "Active";
        public const string Suspended = "Suspended";

        public static bool IsSuspended(string? status) =>
            string.Equals(status, Suspended, StringComparison.OrdinalIgnoreCase);
    }
}
