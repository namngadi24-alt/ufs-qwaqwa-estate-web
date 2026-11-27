using System.ComponentModel.DataAnnotations;

namespace UFSWebApp.Models
{
    public static class ChargeTypes
    {
        public const string Rent = "Rent";
        public const string Water = "Water";
        public const string Electricity = "Electricity";
        public const string Refuse = "Refuse Collection";
        public const string Parking = "Parking";
    }

    public static class TariffCategories
    {
        public const string Rent = "Rent";
        public const string Water = "Water";
        public const string Electricity = "Electricity";
        public const string Refuse = "Refuse";
        public const string Parking = "Parking";
        public static readonly string[] All = { Rent, Water, Electricity, Refuse, Parking };
    }

    public class Tariff
    {
        public int TariffId { get; set; }

        [Required]
        public string Category { get; set; }

        [Required, StringLength(100)]
        public string Name { get; set; }

        /// <summary>Only used for Rent tariffs: the room type this rate applies to.</summary>
        public string? RoomType { get; set; }

        public decimal Rate { get; set; }

        [Required]
        public string Unit { get; set; }

        public DateTime EffectiveFrom { get; set; }
        public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
        public string? UpdatedBy { get; set; }

        public ICollection<TariffChange> Changes { get; set; } = new List<TariffChange>();
    }

    public class TariffChange
    {
        public int TariffChangeId { get; set; }
        public int TariffId { get; set; }
        public Tariff Tariff { get; set; }
        public decimal OldRate { get; set; }
        public decimal NewRate { get; set; }
        public DateTime EffectiveDate { get; set; }
        public DateTime ChangedAt { get; set; } = DateTime.UtcNow;
        public string? ChangedBy { get; set; }
    }

    public class Charge
    {
        public int ChargeId { get; set; }

        public int AccountId { get; set; }
        public Account Account { get; set; }

        public int? RoomId { get; set; }
        public Room? Room { get; set; }

        [Required]
        public string ChargeType { get; set; }
        public string Description { get; set; } = string.Empty;

        /// <summary>First day of the month this charge relates to.</summary>
        public DateTime BillingPeriod { get; set; }
        public DateTime IssueDate { get; set; }
        public DateTime DueDate { get; set; }

        /// <summary>Days, kl, units or vehicles - whatever the rate is multiplied by.</summary>
        public decimal Quantity { get; set; } = 1;
        public string? QuantityUnit { get; set; }
        public decimal Rate { get; set; }
        public decimal Amount { get; set; }
        public decimal AmountPaid { get; set; }
        public bool IsAdvance { get; set; }
        public bool DueSoonNotified { get; set; }
        public bool OverdueNotified { get; set; }

        public decimal Balance => Amount - AmountPaid;
        public bool IsPaid => Balance <= 0.004m;

        public ICollection<PaymentAllocation> Allocations { get; set; } = new List<PaymentAllocation>();
    }

    public static class PaymentStatuses
    {
        public const string Pending = "Pending";
        public const string Successful = "Successful";
        public const string Failed = "Failed";
    }

    public static class PaymentPurposes
    {
        public const string Bills = "Accommodation bills";
        public const string Electricity = "Electricity purchase";
    }

    public class Payment
    {
        public int PaymentId { get; set; }
        public decimal Amount { get; set; }
        public DateTime DateOfPay { get; set; } = DateTime.UtcNow;
        public string PaymentStatus { get; set; }
        public string PaymentPurpose { get; set; } = PaymentPurposes.Bills;
        public string PaymentMethod { get; set; } = "Card";
        public string? CardHolder { get; set; }
        public string? CardBrand { get; set; }
        public string? CardLast4 { get; set; }
        public string? CardExpiry { get; set; }
        public string? GatewayTransactionReference { get; set; }
        public string? ReceiptNumber { get; set; }
        public string? FailureReason { get; set; }
        public string? DecidedBy { get; set; }
        public DateTime? DateDecided { get; set; }

        public int AccountId { get; set; }
        public Account account { get; set; }

        public ICollection<PaymentAllocation> Allocations { get; set; } = new List<PaymentAllocation>();
    }

    public class PaymentAllocation
    {
        public int PaymentAllocationId { get; set; }
        public int PaymentId { get; set; }
        public Payment Payment { get; set; }
        public int ChargeId { get; set; }
        public Charge Charge { get; set; }
        public decimal Amount { get; set; }
    }

    public static class ElectricityTypes
    {
        public const string Purchase = "Purchase";
        public const string Consumption = "Consumption";
    }

    public class ElectricityTransaction
    {
        public int ElectricityTransactionId { get; set; }
        public int AccountId { get; set; }
        public Account Account { get; set; }
        public string TransactionType { get; set; }
        public decimal Units { get; set; }
        public decimal Rate { get; set; }
        public decimal Amount { get; set; }
        public decimal BalanceAfter { get; set; }
        public DateTime Date { get; set; } = DateTime.UtcNow;
        public int? PaymentId { get; set; }
        public Payment? Payment { get; set; }
        public string? RecordedBy { get; set; }
        public string? Note { get; set; }

        /// <summary>Metro Token: unique 20-digit number issued for each electricity purchase.</summary>
        public string? MetroToken { get; set; }
    }

    public class WaterReading
    {
        public int WaterReadingId { get; set; }
        public int AccountId { get; set; }
        public Account Account { get; set; }
        public int? RoomId { get; set; }
        public Room? Room { get; set; }
        public DateTime BillingPeriod { get; set; }
        public decimal UsageKl { get; set; }
        public decimal Rate { get; set; }
        public decimal Amount { get; set; }
        public DateTime DateRecorded { get; set; } = DateTime.UtcNow;
        public string? RecordedBy { get; set; }
        public int? ChargeId { get; set; }
        public Charge? Charge { get; set; }
    }
}
